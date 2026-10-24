using Utility.Logging;
using Geospatial.GDALAssist;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Geospatial.Features;
using Geospatial.IO;
using Geospatial.Vectors;
using Ras.Layers;

namespace Ras.Backend;

/// <summary>Writes real empty Shapefile families without replacing user files.</summary>
public static class NewShapefileService
{
    private sealed record CompletedOperation(string Fingerprint, NewShapefileCreateReceipt Receipt);
    private sealed record ProjectionChoice(Projection? Projection, string? WKT);
    private sealed record PublishMember(string StagedPath, string DestinationPath, string? Revision);
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, CompletedOperation> Completed = [];

    public static NewShapefileCreateReceipt Create(NewShapefileCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        if (!Guid.TryParse(request.OperationID, out Guid operationID))
            throw new ArgumentException("The Shapefile creation operation identity is invalid.");
        if (!Guid.TryParse(request.ProjectSessionID, out _) || !Guid.TryParse(request.MapID, out _))
            throw new ArgumentException("The Shapefile project and map session identity is incomplete.");
        if (string.IsNullOrWhiteSpace(request.ExpectedProjectRevision) ||
            !IsSHA256(request.ExpectedProjectRevision))
            throw new ArgumentException("The current project revision is invalid.");

        string shapeType = request.ShapeType switch
        {
            "Point" or "Polyline" or "Polygon" => request.ShapeType,
            _ => throw new NotSupportedException("Choose Point, Polyline, or Polygon for a new Shapefile.")
        };
        string projectionChoice = request.ProjectionChoice switch
        {
            "Project" or "Local" => request.ProjectionChoice,
            _ => throw new NotSupportedException("Choose the project projection or local project coordinates.")
        };
        string destinationPath = ValidateDestinationPath(request.DestinationPath);
        string fingerprint = Fingerprint(request, destinationPath, shapeType, projectionChoice);

        lock (Gate)
        {
            if (Completed.TryGetValue(operationID, out CompletedOperation? previous))
            {
                if (!previous.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
                    throw new ArgumentException("This Shapefile creation identity was already used for a different request.");
                _ = MapTreeService.ValidateNewShapefileMap(request);
                MapSourceIdentity.VerifyFamily(previous.Receipt.Family);
                return previous.Receipt;
            }

            NewShapefileCreateReceipt receipt = CreateOnce(request, destinationPath, operationID,
                shapeType, projectionChoice);
            Completed.Add(operationID, new(fingerprint, receipt));
            return receipt;
        }
    }

    private static NewShapefileCreateReceipt CreateOnce(NewShapefileCreateRequest request,
        string destinationPath, Guid operationID, string shapeType, string projectionChoice)
    {
        string projectRevision = MapTreeService.ValidateNewShapefileMap(request);
        ProjectionChoice projection = ResolveProjection(request, projectionChoice);
        string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        bool createdDirectory = false;
        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
            createdDirectory = true;
        }

        string baseName = Path.GetFileNameWithoutExtension(destinationPath);
        string lockPath = Path.Combine(destinationDirectory, "." + baseName + ".hecras-shapefile-create.lock");
        string stagingDirectory = Path.Combine(destinationDirectory,
            "." + baseName + ".hecras-shapefile-" + operationID.ToString("N") + ".tmp");
        FileStream? destinationLock = null;
        try
        {
            destinationLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
            EnsureFamilyDoesNotExist(destinationPath);
            Directory.CreateDirectory(stagingDirectory);
            string stagedPath = Path.Combine(stagingDirectory, Path.GetFileName(destinationPath));

            OperationResult write = shapeType switch
            {
                "Point" => ShapefileIO.Write(stagedPath, new List<Point>(), projection: projection.Projection),
                "Polyline" => ShapefileIO.Write(stagedPath, new List<Polyline>(), projection: projection.Projection),
                "Polygon" => ShapefileIO.Write(stagedPath, new List<Polygon>(), projection: projection.Projection),
                _ => throw new InvalidOperationException("The validated geometry type was lost.")
            };
            if (!write.Result)
                throw new InvalidDataException(WriteFailure(shapeType, write));

            string stagedProjectionPath = ShapefileIO.PRJFilename(stagedPath);
            if (projection.Projection is not null && !File.Exists(stagedProjectionPath))
                projection.Projection.ExportEsri(stagedProjectionPath);
            if (projection.Projection is null && File.Exists(stagedProjectionPath))
                throw new InvalidDataException("The Shapefile writer added a projection to a local-coordinate source.");

            string expectedMapType = MapType(shapeType);
            MapShapefileSourceReadResult verifiedSource = MapShapefileImportService.Read(stagedPath);
            if (verifiedSource.MapType != expectedMapType || verifiedSource.FeatureCount != 0)
                throw new InvalidDataException("The retained Shapefile reader did not verify the requested empty geometry family.");

            string[] stagedMembers = PublishOrder(stagedPath, includeProjection: projection.Projection is not null);
            foreach (string path in stagedMembers)
            {
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new InvalidDataException("The Shapefile writer did not create a complete .shp/.shx/.dbf family.");
            }

            // The writer and every staged member are complete before the first
            // public path appears. File.Move is no-overwrite; failures roll back
            // only files this operation itself published.
            MapSourceFamilyCapture stagedFamily = MapTreeService.CaptureShapefileFamily(stagedPath);
            MapSourceFamilyCapture destinationFamily = MapSourceIdentity.RebaseStagedShapefileFamily(
                stagedFamily, destinationPath);
            string? projectionWkt = projection.WKT;
            var receipt = new NewShapefileCreateReceipt(operationID.ToString("D"), request.ProjectSessionID,
                request.MapID, Path.GetFullPath(request.Map.ProjectPath), projectRevision,
                request.Map.ExpectedVisualizationRevision, request.Map.MapIndex,
                request.Map.MapName, request.Map.MapType ?? request.Map.Kind, destinationPath,
                shapeType, projectionChoice, projectionWkt, destinationFamily, DateTimeOffset.UtcNow.ToString("O"));

            return CommitPreparedFamily(stagedPath, destinationPath, stagedFamily, receipt,
                validateBeforeCommit: () =>
                {
                    // Revalidate the captured map while only sidecars are public.
                    // The .shp rename below is the commit point; its move is the
                    // final fallible operation in this method.
                    if (!MapTreeService.ValidateNewShapefileMap(request)
                        .Equals(projectRevision, StringComparison.OrdinalIgnoreCase))
                        throw new SourceFileChangedException();
                });
        }
        finally
        {
            // Lock cleanup happens after the .shp commit. It is deliberately
            // best-effort: a cleanup failure must never turn a complete family
            // into a reported write failure or trigger deletion of its receipt.
            try { destinationLock?.Dispose(); }
            catch (Exception) { }
            try
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
            catch (Exception) { }
            if (createdDirectory)
            {
                try { Directory.Delete(destinationDirectory, recursive: false); }
                catch (Exception) { }
            }
        }
    }

    internal static NewShapefileCreateReceipt CommitPreparedFamily(string stagedPath,
        string destinationPath, MapSourceFamilyCapture stagedFamily, NewShapefileCreateReceipt receipt,
        Action validateBeforeCommit, Action<string, string>? moveFile = null)
    {
        ArgumentNullException.ThrowIfNull(stagedFamily);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(validateBeforeCommit);
        moveFile ??= static (source, destination) => File.Move(source, destination);

        MapSourceIdentity.VerifyFamily(stagedFamily);
        string source = Path.GetFullPath(stagedPath);
        string destination = Path.GetFullPath(destinationPath);
        MapSourceFamilyCapture expectedDestinationFamily =
            MapSourceIdentity.RebaseStagedShapefileFamily(stagedFamily, destination);
        if (!SourceFilePaths.SameSourceFile(receipt.DestinationPath, destination) ||
            !SameFamilyCapture(receipt.Family, expectedDestinationFamily))
            throw new SourceFileChangedException();
        string[][] rolePaths =
        [
            ["source", source, destination],
            ["shx", ShapefileIO.SHXFilename(source), ShapefileIO.SHXFilename(destination)],
            ["dbf", ShapefileIO.DBFFilename(source), ShapefileIO.DBFFilename(destination)],
            ["prj", ShapefileIO.PRJFilename(source), ShapefileIO.PRJFilename(destination)]
        ];
        var stagedByRole = stagedFamily.Files.ToDictionary(file => file.Role, StringComparer.Ordinal);
        if (stagedByRole.Count != rolePaths.Length || rolePaths.Any(row =>
            !stagedByRole.TryGetValue(row[0], out MapSourceFileRevision? file) || file is null ||
            !SourceFilePaths.SameSourceFile(file.InputPath, row[1]) ||
            file.Required != (row[0] != "prj") ||
            file.Exists != (file.Revision is not null) ||
            (file.Required && !file.Exists)))
            throw new SourceFileChangedException();

        PublishMember[] sidecars = rolePaths.Skip(1).Where(row => stagedByRole[row[0]].Exists).Select(row =>
        {
            MapSourceFileRevision file = stagedByRole[row[0]];
            return new PublishMember(file.InputPath, row[2], file.Revision);
        }).ToArray();
        PublishMember sourceMember = new(stagedByRole["source"].InputPath,
            destination, stagedByRole["source"].Revision);
        var published = new PublishMember[sidecars.Length];
        int publishedCount = 0;

        try
        {
            EnsureFamilyDoesNotExist(destination);
            foreach (PublishMember sidecar in sidecars)
            {
                moveFile(sidecar.StagedPath, sidecar.DestinationPath);
                published[publishedCount++] = sidecar;
            }

            // Verify the exact staged bytes at their public sidecar paths and
            // rerun all map guards before making the .shp visible.
            foreach (PublishMember sidecar in sidecars)
            {
                if (!File.Exists(sidecar.DestinationPath) ||
                    !HasExpectedRevision(sidecar.DestinationPath, sidecar.Revision))
                    throw new SourceFileChangedException();
            }
            validateBeforeCommit();
            if (!HasExpectedRevision(sourceMember.StagedPath, sourceMember.Revision))
                throw new SourceFileChangedException();

            // Commit point: after the no-overwrite .shp rename, return the
            // already-complete receipt directly. Do not re-read or recapture
            // files here; any such failure would misreport a successful write.
            moveFile(sourceMember.StagedPath, sourceMember.DestinationPath);
            return receipt;
        }
        catch (Exception publishError)
        {
            List<string> preserved = [];
            for (int index = publishedCount - 1; index >= 0; index--)
            {
                PublishMember member = published[index];
                try
                {
                    if (!File.Exists(member.DestinationPath)) continue;
                    FileAttributes attributes = File.GetAttributes(member.DestinationPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                        !HasExpectedRevision(member.DestinationPath, member.Revision))
                    {
                        preserved.Add(member.DestinationPath);
                        continue;
                    }
                    File.Delete(member.DestinationPath);
                }
                catch (Exception rollbackError)
                {
                    preserved.Add(member.DestinationPath + " (" + rollbackError.Message + ")");
                }
            }

            if (preserved.Count > 0)
            {
                throw new IOException("The Shapefile could not be committed. Published components that changed or could not be removed were preserved: " +
                    string.Join(", ", preserved), publishError);
            }
            throw;
        }
    }

    private static bool HasExpectedRevision(string path, string? expectedRevision)
    {
        if (expectedRevision is null) return false;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string actualRevision = Convert.ToHexString(SHA256.HashData(stream));
        return actualRevision.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameFamilyCapture(MapSourceFamilyCapture left, MapSourceFamilyCapture right) =>
        string.Equals(left.Family, right.Family, StringComparison.Ordinal) &&
        SourceFilePaths.SameSourceFile(left.SourcePath, right.SourcePath) &&
        left.Revision.Equals(right.Revision, StringComparison.OrdinalIgnoreCase) &&
        left.Files.SequenceEqual(right.Files);

    private static ProjectionChoice ResolveProjection(NewShapefileCreateRequest request, string selection)
    {
        using ProjectScope project = OpenProjectScope(request.Map.ProjectPath, request.Map.ProjectXml,
            request.Map.CoreLayerContext);
        ProjectionDefinition definition = project.Project.ProjectionDefinition;
        var evaluation = definition.Evaluate(out Projection? projection, out _);
        if (!evaluation.Result)
            throw new InvalidOperationException("The project projection is invalid. Fix the project projection before creating a map Shapefile.");

        if (selection == "Project")
        {
            if (definition.Type == ProjectionType.Local || projection is null)
                throw new ArgumentException("This project uses local coordinates. Select Local project coordinates.");
            string wkt = projection.GetWKT();
            if (string.IsNullOrWhiteSpace(wkt))
                throw new InvalidDataException("The project projection did not provide a usable WKT definition.");
            return new(projection, wkt);
        }

        if (definition.Type != ProjectionType.Local || projection is not null)
            throw new ArgumentException("Local coordinates can be selected only for a Local-coordinate project.");
        return new(null, null);
    }

    private sealed class ProjectScope(Project project) : IDisposable
    {
        public Project Project { get; } = project;
        public void Dispose() => MapResultChartService.DisposeProject(Project);
    }

    private static ProjectScope OpenProjectScope(string projectPath, string? projectXml,
        CoreLayerRequestContext? coreLayerContext)
    {
        XElement? projectMetadata = string.IsNullOrWhiteSpace(projectXml)
            ? null : XElement.Parse(projectXml);
        return new(CoreLayerImportService.OpenProjectWithMetadata(
            Path.GetFullPath(projectPath), projectMetadata, coreLayerContext));
    }

    private static string ValidateDestinationPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("Choose an absolute destination path for the new Shapefile.", nameof(value));
        string path = Path.GetFullPath(value);
        if (!Path.GetExtension(path).Equals(".shp", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The new Shapefile destination must end in .shp.", nameof(value));
        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(path)))
            throw new ArgumentException("Choose a file name for the new Shapefile.", nameof(value));
        return Path.ChangeExtension(path, ".shp");
    }

    private static void EnsureFamilyDoesNotExist(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath)!;
        string[] names = FamilyPaths(sourcePath, includeProjection: true)
            .Select(path => Path.GetFileName(path)!).ToArray();
        if (!Directory.Exists(directory)) return;
        HashSet<string> existing = Directory.EnumerateFileSystemEntries(directory)
            .Select(path => Path.GetFileName(path)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? collision = names.FirstOrDefault(existing.Contains);
        if (collision is not null)
            throw new IOException($"A Shapefile component already exists at '{Path.Combine(directory, collision)}'. Choose a different destination.");
    }

    private static string[] FamilyPaths(string sourcePath, bool includeProjection)
    {
        var paths = new List<string>
        {
            sourcePath,
            ShapefileIO.SHXFilename(sourcePath),
            ShapefileIO.DBFFilename(sourcePath)
        };
        if (includeProjection) paths.Add(ShapefileIO.PRJFilename(sourcePath));
        return paths.ToArray();
    }

    private static string[] PublishOrder(string sourcePath, bool includeProjection)
    {
        // The conventional .shp file is the visibility/commit marker. Publish
        // every sidecar first and rename the .shp last, so readers never see a
        // primary file before the family is complete.
        string[] family = FamilyPaths(sourcePath, includeProjection);
        return family.Skip(1).Append(family[0]).ToArray();
    }

    private static string MapType(string shapeType) => shapeType switch
    {
        "Point" => "PointsVector",
        "Polyline" => "PolylinesVector",
        "Polygon" => "PolygonsVector",
        _ => throw new ArgumentOutOfRangeException(nameof(shapeType))
    };

    private static string WriteFailure(string shapeType, Utility.Logging.OperationResult result)
    {
        string detail = result.GetConcatenatedMessages();
        return string.IsNullOrWhiteSpace(detail)
            ? $"The retained Shapefile writer could not create an empty {shapeType} family."
            : detail;
    }

    private static string Fingerprint(NewShapefileCreateRequest request, string destinationPath,
        string shapeType, string projectionChoice)
    {
        string material = string.Join("\n", request.ProjectSessionID, request.MapID,
            SourceFilePaths.Canonical(destinationPath), request.ExpectedProjectRevision.ToUpperInvariant(),
            request.Map.ProjectPath, request.Map.ProjectXmlSHA ?? "", request.Map.ExpectedVisualizationRevision ?? "",
            request.Map.MapIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            request.Map.MapName ?? "", request.Map.MapType ?? request.Map.Kind,
            shapeType, projectionChoice);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static bool IsSHA256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');
}
