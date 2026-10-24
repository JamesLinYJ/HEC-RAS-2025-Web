using System.Security.Cryptography;
using System.Text;
using Ras.Layers;
using Ras.Observables;
using Utility.Files;

namespace Ras.Backend;

public sealed record GeometryBackupRestoreRequest(
    string ProjectPath,
    string ProjectXml,
    string BackupPath,
    string BackupCanonicalPath,
    string ExpectedBackupRevision,
    string ParentGeometryPath,
    string ExpectedParentGeometryRevision,
    string ExpectedProjectRevision,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record GeometryBackupRestoreReceipt(
    string ProjectPath,
    string BackupPath,
    string BackupRevision,
    string ParentGeometryPath,
    string ParentGeometryRevision,
    string ProjectRevision,
    string NewAssetPath,
    string NewName,
    string NewAssetRevision,
    string ProjectXmlFingerprint,
    string? CleanupWarning = null);

public sealed record GeometryNamedBackupRequest(
    string ProjectPath,
    string ProjectXml,
    string ExpectedProjectRevision,
    string GeometryPath,
    string GeometryCanonicalPath,
    string ExpectedGeometryRevision,
    string Name,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record GeometryNamedBackupReceipt(
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlFingerprint,
    string GeometryPath,
    string GeometryRevision,
    string BackupPath,
    string BackupRevision);

/// <summary>
/// Restores an exact, selected Geometry backup by copying its HDF into a
/// private staging directory and publishing it under the source-compatible
/// unique Geometry filename. The project XML and both input files remain
/// untouched; discovery and association are supplied by the next project
/// catalog reload.
/// </summary>
public static class GeometryBackupService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static GeometryBackupRestoreReceipt Restore(GeometryBackupRestoreRequest request)
    {
        using var context = new RestoreContext(request);
        string projectRevision = ReadStableRevision(context.Project.Filename).Revision;
        ValidateRevision(request.ExpectedProjectRevision);
        if (!HashEquals(projectRevision, request.ExpectedProjectRevision))
            throw new SourceFileChangedException();
        string backupRevision = VerifySelectedFile(request.BackupPath, request.BackupCanonicalPath,
            request.ExpectedBackupRevision);
        string parentRevision = VerifySelectedFile(context.ParentGeometry.Filename,
            request.ParentGeometryPath, request.ExpectedParentGeometryRevision);

        string sourceName = Path.GetFileNameWithoutExtension(request.BackupPath);
        string desiredName = context.ParentGeometry.Name + " - " + sourceName;
        // BackupCatalogSummary copies to Geometry.MakeFilename directly. Its
        // generated basename may exceed the model-creation dialog's 64-char
        // name limit; restoring an existing backup must retain that filename.
        string baseTarget = Geometry.MakeFilename(context.Project.ProjectDirectory, desiredName);
        string targetDirectory = Path.GetDirectoryName(baseTarget)
            ?? throw new IOException("The Geometry destination folder is unavailable.");
        string canonicalGeometryDirectory = SourceFilePaths.Canonical(context.Project.GeometriesDirectory);
        if (!PathComparer.Equals(SourceFilePaths.Canonical(targetDirectory), canonicalGeometryDirectory))
            throw new IOException("The Geometry destination is outside the current project's Geometry folder.");
        string canonicalBaseTarget = Path.Combine(canonicalGeometryDirectory, Path.GetFileName(baseTarget));

        string token = Guid.NewGuid().ToString("N");
        string canonicalStageDirectory = Path.Combine(canonicalGeometryDirectory, ".geometry-backup-restore-" + token);
        string marker = Path.Combine(canonicalStageDirectory, ".geometry-backup-restore-owner");
        string stagedAsset = Path.Combine(canonicalStageDirectory, "restored-geometry.h5");
        bool stageDirectoryCreated = false;
        bool markerCreated = false;
        GeometryBackupRestoreReceipt? receipt = null;
        try
        {
            if (Directory.Exists(canonicalStageDirectory) || File.Exists(canonicalStageDirectory))
                throw new IOException("The Geometry restore staging folder is already in use.");
            Directory.CreateDirectory(canonicalStageDirectory);
            stageDirectoryCreated = true;
            using (var markerStream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                markerCreated = true;
                byte[] markerBytes = Encoding.UTF8.GetBytes(token);
                markerStream.Write(markerBytes, 0, markerBytes.Length);
                markerStream.Flush(flushToDisk: true);
            }

            string copiedRevision = CopyAndHash(request.BackupPath, stagedAsset);
            if (!HashEquals(copiedRevision, request.ExpectedBackupRevision))
                throw new SourceFileChangedException();
            if (!HashEquals(ReadStableRevision(stagedAsset).Revision, request.ExpectedBackupRevision))
                throw new SourceFileChangedException();

            // A backup is a Geometry HDF, not an arbitrary file copy. Open it
            // with the retained source model before selecting a final path.
            using (var restored = new Geometry(stagedAsset))
            {
                if (restored.HasLoadErrors)
                    throw new InvalidDataException("The selected Geometry backup could not be reopened.");
            }

            if (!HashEquals(ReadStableRevision(request.BackupPath, request.BackupCanonicalPath).Revision,
                    request.ExpectedBackupRevision) ||
                !HashEquals(ReadStableRevision(context.ParentGeometry.Filename, request.ParentGeometryPath).Revision,
                    request.ExpectedParentGeometryRevision) ||
                !HashEquals(ReadStableRevision(context.Project.Filename).Revision, projectRevision))
                throw new SourceFileChangedException();

            RequireDestinationDirectory(context.Project.GeometriesDirectory, canonicalGeometryDirectory);
            RequireDestinationDirectory(targetDirectory, canonicalGeometryDirectory);
            string target = PublishWithoutOverwrite(stagedAsset, canonicalBaseTarget);
            receipt = new GeometryBackupRestoreReceipt(
                Path.GetFullPath(context.Project.Filename),
                Path.GetFullPath(request.BackupPath),
                backupRevision,
                SourceFilePaths.Canonical(context.ParentGeometry.Filename),
                parentRevision,
                projectRevision,
                Path.GetFullPath(target),
                Path.GetFileNameWithoutExtension(target),
                backupRevision,
                HashText(request.ProjectXml));
        }
        finally
        {
            if (stageDirectoryCreated && Directory.Exists(canonicalStageDirectory))
            {
                try
                {
                    string[] entries = Directory.EnumerateFileSystemEntries(canonicalStageDirectory).ToArray();
                    bool onlyOwnedEntries = entries.All(entry => PathComparer.Equals(Path.GetFullPath(entry), marker) ||
                        PathComparer.Equals(Path.GetFullPath(entry), stagedAsset));
                    bool ownsMarker = markerCreated && File.Exists(marker) && onlyOwnedEntries &&
                        string.Equals(File.ReadAllText(marker), token, StringComparison.Ordinal);
                    bool ownsPartialMarker = markerCreated && onlyOwnedEntries;
                    bool emptyUnmarkedDirectory = !markerCreated &&
                        !Directory.EnumerateFileSystemEntries(canonicalStageDirectory).Any();
                    if (ownsMarker || ownsPartialMarker || emptyUnmarkedDirectory)
                        Directory.Delete(canonicalStageDirectory, recursive: true);
                }
                catch (Exception)
                {
                    if (receipt is { } publishedReceipt)
                    {
                        receipt = publishedReceipt with
                        {
                            CleanupWarning = "Geometry restore completed, but its temporary folder could not be removed."
                        };
                    }
                }
            }
        }
        return receipt ?? throw new InvalidOperationException("The Geometry restore did not publish an asset.");
    }

    /// <summary>
    /// Runs the retained ObservableGeometry named-backup action against the
    /// exact selected source. The explicit source name is kept as entered;
    /// duplicate names follow FileHelpers.UniqueFilename's numbered naming.
    /// </summary>
    public static GeometryNamedBackupReceipt CreateNamed(GeometryNamedBackupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("Open a saved project before creating a Geometry backup.");
        if (string.IsNullOrEmpty(request.Name))
            throw new ArgumentException("Enter a Geometry backup name.");
        if (request.Name.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("Geometry backup names cannot contain path separators.");
        ValidateRevision(request.ExpectedProjectRevision);
        ValidateRevision(request.ExpectedGeometryRevision);

        string projectPath = Path.GetFullPath(request.ProjectPath);
        if (!File.Exists(projectPath)) throw new FileNotFoundException("The project file was not found.", projectPath);
        string geometryCanonicalPath = SourceFilePaths.Canonical(request.GeometryCanonicalPath);
        var projectFileBefore = ReadStableRevision(projectPath);
        if (!PathComparer.Equals(projectFileBefore.CanonicalPath, SourceFilePaths.Canonical(request.ProjectPath)) ||
            !HashEquals(projectFileBefore.Revision, request.ExpectedProjectRevision))
            throw new SourceFileChangedException();

        Project project = CoreLayerImportService.OpenProject(projectPath, request.ProjectXml,
            request.CoreLayerContext);
        string? createdBackupPath = null;
        string? expectedBackupDirectory = null;
        try
        {
            var observableProject = new ObservableProject(project, Utility.SynchronizationContext.None);
            ObservableGeometry[] matches = observableProject.Geometries.Where(geometry =>
                PathComparer.Equals(SourceFilePaths.Canonical(geometry.Filename), geometryCanonicalPath)).ToArray();
            if (matches.Length != 1) throw new SourceFileChangedException();
            ObservableGeometry geometry = matches[0];
            string canonicalGeometryPath = SourceFilePaths.Canonical(geometry.Filename);
            if (!PathComparer.Equals(canonicalGeometryPath, geometryCanonicalPath) ||
                !PathComparer.Equals(canonicalGeometryPath, SourceFilePaths.Canonical(request.GeometryPath)))
                throw new SourceFileChangedException();

            var geometryBefore = ReadStableRevision(geometry.Filename, canonicalGeometryPath);
            if (!HashEquals(geometryBefore.Revision, request.ExpectedGeometryRevision))
                throw new SourceFileChangedException();

            expectedBackupDirectory = SourceFilePaths.Canonical(geometry.BackupDirectory());
            createdBackupPath = geometry.CheckCreateBackup(request.Name);
            if (string.IsNullOrEmpty(createdBackupPath))
                throw new InvalidOperationException("Could not create Geometry backup.");

            string canonicalBackupPath = SourceFilePaths.Canonical(createdBackupPath);
            if (!PathComparer.Equals(SourceFilePaths.Canonical(Path.GetDirectoryName(canonicalBackupPath) ?? string.Empty),
                    expectedBackupDirectory) ||
                !string.Equals(Path.GetExtension(canonicalBackupPath), ".h5", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The Geometry backup was created outside its source backup folder.");

            var backupFile = ReadStableRevision(canonicalBackupPath);
            using (var reopenedBackup = new Geometry(canonicalBackupPath))
                if (reopenedBackup.HasLoadErrors)
                    throw new InvalidDataException("The named Geometry backup could not be reopened.");
            var geometryAfter = ReadStableRevision(geometry.Filename, canonicalGeometryPath);
            var projectFileAfter = ReadStableRevision(projectPath, projectFileBefore.CanonicalPath);
            if (!HashEquals(geometryAfter.Revision, geometryBefore.Revision) ||
                !HashEquals(projectFileAfter.Revision, projectFileBefore.Revision))
                throw new SourceFileChangedException();

            return new GeometryNamedBackupReceipt(
                projectFileBefore.CanonicalPath,
                projectFileBefore.Revision,
                HashText(request.ProjectXml),
                canonicalGeometryPath,
                geometryAfter.Revision,
                canonicalBackupPath,
                backupFile.Revision);
        }
        catch
        {
            // CheckCreateBackup owns the exact newly returned path. If a
            // post-write identity check fails, remove only that new file and
            // only while it remains inside this Geometry's backup folder.
            if (createdBackupPath != null && expectedBackupDirectory != null)
            {
                try
                {
                    string canonicalCreated = SourceFilePaths.Canonical(createdBackupPath);
                    string canonicalParent = SourceFilePaths.Canonical(Path.GetDirectoryName(canonicalCreated) ?? string.Empty);
                    if (PathComparer.Equals(canonicalParent, expectedBackupDirectory) && File.Exists(canonicalCreated))
                        File.Delete(canonicalCreated);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        finally
        {
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                .Concat(project.ClassificationLayers)) layer.Dispose();
            foreach (Result result in project.Results) result.Dispose();
        }
    }

    private static void RequireDestinationDirectory(string path, string expectedCanonicalPath)
    {
        if (!PathComparer.Equals(SourceFilePaths.Canonical(path), expectedCanonicalPath))
            throw new SourceFileChangedException();
    }

    private static string VerifySelectedFile(string path, string expectedCanonicalPath, string expectedRevision)
    {
        ValidateRevision(expectedRevision);
        (string canonicalPath, string revision) = ReadStableRevision(path);
        if (!PathComparer.Equals(canonicalPath, expectedCanonicalPath) || !HashEquals(revision, expectedRevision))
            throw new SourceFileChangedException();
        return revision;
    }

    private static string PublishWithoutOverwrite(string stagedAsset, string baseTarget)
    {
        string directory = Path.GetDirectoryName(baseTarget)!;
        string basename = Path.GetFileNameWithoutExtension(baseTarget);
        string extension = Path.GetExtension(baseTarget);
        for (int index = 0; index < 1000; index++)
        {
            string candidate = index == 0
                ? baseTarget
                : Path.Combine(directory, basename + " (" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + extension);
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            try
            {
                File.Move(stagedAsset, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
            {
                // Another process claimed this source-compatible name after
                // the check; advance to the next unique name without touching it.
            }
        }
        throw new IOException("Could not find an unused Geometry filename.");
    }

    private static string CopyAndHash(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }
        output.Flush(flushToDisk: true);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static (string CanonicalPath, string Revision) ReadStableRevision(string path,
        string? expectedCanonicalPath = null)
    {
        string canonicalBefore = SourceFilePaths.Canonical(path);
        if (expectedCanonicalPath != null && !PathComparer.Equals(canonicalBefore, expectedCanonicalPath))
            throw new SourceFileChangedException();

        var file = new FileInfo(path);
        long lengthBefore = file.Length;
        DateTime lastWriteBefore = file.LastWriteTimeUtc;
        string revision;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            revision = Convert.ToHexString(SHA256.HashData(stream));
        file.Refresh();
        string canonicalAfter = SourceFilePaths.Canonical(path);
        if (!PathComparer.Equals(canonicalBefore, canonicalAfter) ||
            (expectedCanonicalPath != null && !PathComparer.Equals(canonicalAfter, expectedCanonicalPath)) ||
            file.Length != lengthBefore || file.LastWriteTimeUtc != lastWriteBefore)
            throw new SourceFileChangedException();
        return (canonicalAfter, revision);
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool HashEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void ValidateRevision(string revision)
    {
        if (revision.Length != 64 || revision.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The selected file revision is invalid.");
    }

    private sealed class RestoreContext : IDisposable
    {
        public Project Project { get; }
        public Geometry ParentGeometry { get; }

        public RestoreContext(GeometryBackupRestoreRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectPath) || string.IsNullOrWhiteSpace(request.ProjectXml))
                throw new ArgumentException("Open a saved project before restoring a Geometry backup.");
            string projectPath = Path.GetFullPath(request.ProjectPath);
            if (!File.Exists(projectPath)) throw new FileNotFoundException("The project file was not found.", projectPath);

            Project = CoreLayerImportService.OpenProject(projectPath, request.ProjectXml,
                request.CoreLayerContext);
            try
            {
                Geometry[] matches = Project.Geometries.Where(item =>
                    SourceFilePaths.SameSourceFile(item.Filename, request.ParentGeometryPath)).ToArray();
                if (matches.Length != 1) throw new SourceFileChangedException();
                ParentGeometry = matches[0];

                string canonicalParent = SourceFilePaths.Canonical(ParentGeometry.Filename);
                if (!PathComparer.Equals(canonicalParent, request.ParentGeometryPath))
                    throw new SourceFileChangedException();
                string backupDirectory = Path.Combine(Path.GetDirectoryName(ParentGeometry.Filename)!,
                    Path.GetFileNameWithoutExtension(ParentGeometry.Filename) + " Backups");
                string expectedBackupDirectory = Path.GetFullPath(backupDirectory);
                string backupPath = Path.GetFullPath(request.BackupPath);
                if (!PathComparer.Equals(Path.GetDirectoryName(backupPath), expectedBackupDirectory) ||
                    !Directory.Exists(expectedBackupDirectory) ||
                    !Directory.GetFiles(expectedBackupDirectory).Any(path => SourceFilePaths.SameSourceFile(path, backupPath)))
                    throw new SourceFileChangedException();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (CoreLayerBase layer in Project.Plans.Cast<CoreLayerBase>().Concat(Project.Geometries)
                .Concat(Project.Terrains).Concat(Project.BoundaryConditions).Concat(Project.NValues)
                .Concat(Project.ClassificationLayers)) layer.Dispose();
            foreach (Result result in Project.Results) result.Dispose();
        }
    }
}
