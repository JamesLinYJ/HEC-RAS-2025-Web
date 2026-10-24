using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using H5Assist;
using Ras.Layers;
using Utility.Units;

namespace Ras.Backend;

public sealed record WorkspaceArchiveImportRequest(
    string SourcePath,
    string DestinationDirectory,
    string SourceRevision);

public sealed record WorkspaceCoreHdfImportRequest(
    string SourcePath,
    string DestinationDirectory,
    string SourceRevision);

public sealed record WorkspacePreparedProject(
    string SourcePath,
    string SourceRevision,
    string DestinationDirectory,
    string ProjectPath,
    string ProjectName,
    string ImportKind,
    string? ImportedAssetPath,
    string? ImportedAssetRevision);

/// <summary>
/// Prepares user-selected ZIP and HDF inputs as new, isolated project folders.
/// The source is never modified and existing destinations are never replaced.
/// </summary>
public static class WorkspaceFileImportService
{
    private const int MaximumArchiveEntries = 250_000;
    private const long MaximumArchiveExpandedBytes = 2L * 1024 * 1024 * 1024 * 1024;
    private const int CopyBufferSize = 1024 * 1024;

    public static WorkspacePreparedProject PrepareArchive(WorkspaceArchiveImportRequest request) =>
        PrepareArchiveCore(request, CancellationToken.None);

    internal static Task<WorkspacePreparedProject> PrepareArchiveAsync(
        WorkspaceArchiveImportRequest request,
        Action<BackendOperationProgress>? report = null,
        BackendDuplexControlReader? duplexControls = null) =>
        RunWithDuplexCancellationAsync(token => PrepareArchiveCore(request, token),
            receipt => RemovePreparedDirectory(receipt, request.DestinationDirectory), report, duplexControls);

    public static WorkspacePreparedProject PrepareCoreHdf(WorkspaceCoreHdfImportRequest request) =>
        PrepareCoreHdfCore(request, CancellationToken.None);

    internal static Task<WorkspacePreparedProject> PrepareCoreHdfAsync(
        WorkspaceCoreHdfImportRequest request,
        Action<BackendOperationProgress>? report = null,
        BackendDuplexControlReader? duplexControls = null) =>
        RunWithDuplexCancellationAsync(token => PrepareCoreHdfCore(request, token),
            receipt => RemovePreparedDirectory(receipt, request.DestinationDirectory), report, duplexControls);

    private static WorkspacePreparedProject PrepareArchiveCore(WorkspaceArchiveImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string sourcePath = ValidateSource(request.SourcePath, ".zip", request.SourceRevision, cancellationToken);
        string destinationRoot = ValidateDestinationDirectory(request.DestinationDirectory);
        string staging = Path.Combine(destinationRoot, ".hecras-import-" + Guid.NewGuid().ToString("N") + ".partial");
        Directory.CreateDirectory(staging);
        string? publishedDirectory = null;
        bool completed = false;
        try
        {
            ExtractArchiveSafely(sourcePath, staging, cancellationToken);
            var projects = new List<string>();
            foreach (string path in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetExtension(path).Equals(".ras", StringComparison.OrdinalIgnoreCase))
                    projects.Add(path);
            }
            if (projects.Count != 1)
                throw new InvalidDataException($"The archive contains {projects.Count} HEC-RAS project files; exactly one is required.");

            VerifySourceRevision(sourcePath, request.SourceRevision, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string finalDirectory = CreateUniqueDirectoryName(destinationRoot,
                Path.GetFileNameWithoutExtension(sourcePath));
            string relativeProjectPath = Path.GetRelativePath(staging, projects[0]);
            Directory.Move(staging, finalDirectory);
            publishedDirectory = finalDirectory;
            cancellationToken.ThrowIfCancellationRequested();
            string projectPath = Path.GetFullPath(Path.Combine(finalDirectory, relativeProjectPath));
            if (!File.Exists(projectPath))
                throw new IOException("The extracted project file is missing from the published import folder.");

            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
            return new(sourcePath, request.SourceRevision, finalDirectory, projectPath,
                Path.GetFileNameWithoutExtension(projectPath), "archive", null, null);
        }
        finally
        {
            if (!completed)
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                if (publishedDirectory != null && Directory.Exists(publishedDirectory))
                    Directory.Delete(publishedDirectory, recursive: true);
            }
        }
    }

    private static WorkspacePreparedProject PrepareCoreHdfCore(WorkspaceCoreHdfImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string sourcePath = ValidateSource(request.SourcePath, ".h5", request.SourceRevision, cancellationToken);
        string destinationRoot = ValidateDestinationDirectory(request.DestinationDirectory);

        string projectName;
        string wkt;
        UnitSystem units;
        string importKind;
        using (var reader = new H5Reader(sourcePath))
        {
            if (!reader.SuccessfullyOpened)
                throw new InvalidDataException("The selected file is not a readable HDF5 file.");

            Project.LoadProjectMetadataFromH5Header(reader, out projectName, out wkt, out units);
            // This order matches RasMainWindow.TryOpenQuickProjectFromCoreFile.
            // Some HDF files contain more than one recognized group; the source
            // treats them as Result before Geometry before BoundaryCondition.
            if (Result.IsValid(reader)) importKind = "result";
            else if (Geometry.IsValid(reader)) importKind = "geometry";
            else if (BoundaryCondition.IsValid(reader)) importKind = "boundaryCondition";
            else throw new InvalidDataException("The HDF5 file is not a valid RAS Result, Geometry, or Boundary Condition.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        VerifySourceRevision(sourcePath, request.SourceRevision, cancellationToken);
        projectName = SanitizeProjectName(projectName, sourcePath);
        string staging = Path.Combine(destinationRoot, ".hecras-import-" + Guid.NewGuid().ToString("N") + ".partial");
        Directory.CreateDirectory(staging);
        string? publishedDirectory = null;
        bool completed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string stagedProjectPath = Path.Combine(staging, projectName + ".ras");
            Project project = string.IsNullOrWhiteSpace(wkt)
                ? Project.CreateNew(stagedProjectPath, units)
                : Project.CreateNew(stagedProjectPath, new ProjectionDefinition(ProjectionType.WKT, wkt));
            string targetDirectory = importKind switch
            {
                "result" => project.ResultsDirectory,
                "geometry" => project.GeometriesDirectory,
                "boundaryCondition" => project.BoundaryConditionDirectory,
                _ => throw new InvalidOperationException("The validated HDF type is not supported.")
            };
            Directory.CreateDirectory(targetDirectory);
            string importedAssetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
            CopyFileCreateNew(sourcePath, importedAssetPath, cancellationToken);
            string importedRevision = ComputeRevision(importedAssetPath, cancellationToken);
            VerifySourceRevision(sourcePath, request.SourceRevision, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            string finalDirectory = CreateUniqueDirectoryName(destinationRoot, projectName);
            string relativeAssetPath = Path.GetRelativePath(staging, importedAssetPath);
            string projectFilename = Path.GetFileName(stagedProjectPath);
            Directory.Move(staging, finalDirectory);
            publishedDirectory = finalDirectory;
            cancellationToken.ThrowIfCancellationRequested();
            string projectPath = Path.Combine(finalDirectory, projectFilename);
            string finalAssetPath = Path.GetFullPath(Path.Combine(finalDirectory, relativeAssetPath));
            if (!File.Exists(projectPath) || !File.Exists(finalAssetPath)
                || !string.Equals(ComputeRevision(finalAssetPath, cancellationToken), importedRevision, StringComparison.Ordinal))
                throw new IOException("The imported HDF project did not retain its complete source file.");

            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
            return new(sourcePath, request.SourceRevision, finalDirectory, projectPath,
                projectName, importKind, finalAssetPath, importedRevision);
        }
        finally
        {
            if (!completed)
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                if (publishedDirectory != null && Directory.Exists(publishedDirectory))
                    Directory.Delete(publishedDirectory, recursive: true);
            }
        }
    }

    private static async Task<WorkspacePreparedProject> RunWithDuplexCancellationAsync(
        Func<CancellationToken, WorkspacePreparedProject> prepare,
        Action<WorkspacePreparedProject> removePrepared,
        Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        _ = report;
        using var cancellation = new CancellationTokenSource();
        Task? controlPump = duplexControls is null ? null : Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls.ReadAllAsync(cancellation.Token)
                                   .ConfigureAwait(false))
                {
                    JsonElement control = frame.Control;
                    if (control.TryGetProperty("type", out JsonElement type)
                        && string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                    {
                        cancellation.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch
            {
                // A malformed control stream cannot be allowed to leave a
                // newly published project behind as though the request had
                // succeeded.
                cancellation.Cancel();
            }
        }, CancellationToken.None);

        try
        {
            WorkspacePreparedProject prepared = await Task.Run(
                () => prepare(cancellation.Token), CancellationToken.None).ConfigureAwait(false);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                return prepared;
            }
            catch
            {
                removePrepared(prepared);
                throw;
            }
        }
        finally
        {
            cancellation.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }
        }
    }

    private static string ValidateSource(string path, string requiredExtension,
        string expectedRevision, CancellationToken cancellationToken, params string[] additionalExtensions)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute path to the source file.");
        if (string.IsNullOrWhiteSpace(expectedRevision) || expectedRevision.Length != 64
            || expectedRevision.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The selected source file revision is required.");

        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (!extension.Equals(requiredExtension, StringComparison.OrdinalIgnoreCase)
            && !additionalExtensions.Any(value => extension.Equals(value, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Choose a {requiredExtension} source file.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The selected source file could not be found.", fullPath);
        VerifySourceRevision(fullPath, expectedRevision, cancellationToken);
        return fullPath;
    }

    private static string ValidateDestinationDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute destination folder.");
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException("The selected destination folder does not exist.");
        return fullPath;
    }

    private static void ExtractArchiveSafely(string sourcePath, string destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new InvalidDataException("The archive contains too many entries to extract safely.");

        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(name))
                throw new InvalidDataException("The archive contains a rooted or empty path.");
            string[] segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
                throw new InvalidDataException("The archive contains a path that escapes its destination folder.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The archive contains a symbolic link, which cannot be imported safely.");
            string target = Path.GetFullPath(Path.Combine(destination, Path.Combine(segments)));
            if (!target.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("The archive contains a path that escapes its destination folder.");
            bool isDirectory = name.EndsWith("/", StringComparison.Ordinal);
            string collisionKey = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!seen.Add(collisionKey))
                throw new InvalidDataException("The archive contains duplicate paths that cannot be represented safely.");

            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumArchiveExpandedBytes)
                throw new InvalidDataException("The archive is larger than the supported extraction limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream source = entry.Open();
            using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            CopyStream(source, output, cancellationToken);
            if (output.Length != entry.Length)
                throw new InvalidDataException("An archive entry did not extract to its declared length.");
        }
    }

    private static void CopyFileCreateNew(string sourcePath, string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        CopyStream(source, destination, cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    private static void CopyStream(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[CopyBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            destination.Write(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void RemovePreparedDirectory(WorkspacePreparedProject receipt, string destinationDirectory)
    {
        string root = Path.GetFullPath(destinationDirectory);
        string directory = Path.GetFullPath(receipt.DestinationDirectory);
        string project = Path.GetFullPath(receipt.ProjectPath);
        if (!Directory.Exists(root) || !Directory.Exists(directory)
            || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
            || !string.Equals(Path.GetDirectoryName(directory), root, PathComparison)
            || !project.StartsWith(directory + Path.DirectorySeparatorChar, PathComparison)
            || !File.Exists(project)
            || (File.GetAttributes(project) & FileAttributes.ReparsePoint) != 0
            || !string.Equals(Path.GetFileNameWithoutExtension(project), receipt.ProjectName, StringComparison.Ordinal))
            return;
        Directory.Delete(directory, recursive: true);
    }

    private static string CreateUniqueDirectoryName(string root, string stem)
    {
        string safeStem = SanitizeProjectName(stem, Path.Combine(root, "Project.ras"));
        for (int attempt = 0; attempt < 100; attempt++)
        {
            string candidate = Path.Combine(root, safeStem + "_" + Guid.NewGuid().ToString("N")[..8]);
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not find an unused destination folder for the imported project.");
    }

    private static string SanitizeProjectName(string? value, string sourcePath)
    {
        string name = string.IsNullOrWhiteSpace(value) ? Path.GetFileNameWithoutExtension(sourcePath) : value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().Trim('.');
        if (name.Length > 120) name = name[..120].TrimEnd();
        return string.IsNullOrWhiteSpace(name) ? "Imported Project" : name;
    }

    private static void VerifySourceRevision(string path, string expected,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path) || !string.Equals(ComputeRevision(path, cancellationToken), expected, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static string ComputeRevision(string path, CancellationToken cancellationToken = default)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[CopyBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            hasher.AppendData(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
