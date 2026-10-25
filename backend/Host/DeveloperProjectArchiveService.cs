using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Files;
using Utility.Progress;

namespace Ras.Backend;

public sealed record DeveloperProjectArchiveRequest(
    string ProjectPath,
    bool IncludeResults,
    string ExpectedProjectSHA256);

public sealed record DeveloperProjectArchiveResult(
    string State,
    string CleanupToken,
    string ArchivePath,
    string ProjectPath,
    string SourceProjectSHA256,
    string ProjectSHA256,
    string InventorySHA256,
    int EntryCount,
    bool IncludeResults);

public sealed record DeveloperProjectArchiveCleanupRequest(string CleanupToken);
public sealed record DeveloperProjectArchiveCleanupResult(bool Cleaned);

/// <summary>
/// Creates a private, source-compatible upload archive using Project.ZipFullProject.
/// The returned UUID is the sole cleanup capability; callers never choose a path
/// for the service to delete.
/// </summary>
public static class DeveloperProjectArchiveService
{
    private const int HashBufferSize = 1024 * 1024;

    public static DeveloperProjectArchiveResult Prepare(
        DeveloperProjectArchiveRequest request,
        Action<BackendOperationProgress>? report = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArchiveOperation operation = CreateOperation(request);
        return PrepareCore(request, operation, report, cancellationToken);
    }

    internal static async Task<DeveloperProjectArchiveResult> PrepareDuplexAsync(
        DeveloperProjectArchiveRequest request,
        Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArchiveOperation operation = CreateOperation(request);
        if (duplexControls is null)
            return await Task.Run(() => PrepareCore(request, operation, report, CancellationToken.None))
                .ConfigureAwait(false);

        using var cancellation = new CancellationTokenSource();
        object controlErrorGate = new();
        Exception? controlError = null;
        using var stopControlPump = new CancellationTokenSource();
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls
                                   .ReadAllAsync(stopControlPump.Token).ConfigureAwait(false))
                {
                    JsonElement control = frame.Control;
                    if (control.TryGetProperty("type", out JsonElement type) &&
                        type.ValueKind == JsonValueKind.String &&
                        string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                    {
                        cancellation.Cancel();
                        return;
                    }
                    throw new InvalidDataException("The developer archive received an invalid cancellation control.");
                }
            }
            catch (OperationCanceledException) when (stopControlPump.IsCancellationRequested) { }
            catch (Exception error)
            {
                lock (controlErrorGate) controlError = error;
                cancellation.Cancel();
            }
        }, CancellationToken.None);

        try
        {
            try
            {
                return await Task.Run(() => PrepareCore(request, operation, report, cancellation.Token),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Exception? invalidControl;
                lock (controlErrorGate) invalidControl = controlError;
                if (invalidControl is not null)
                    throw new InvalidDataException("The developer archive received an invalid cancellation control.",
                        invalidControl);
                return CancelledResult(request, operation);
            }
        }
        finally
        {
            stopControlPump.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch { }
            cancellation.Dispose();
        }
    }

    public static DeveloperProjectArchiveCleanupResult Cleanup(
        DeveloperProjectArchiveCleanupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParseExact(request.CleanupToken, "D", out Guid token))
            return new(false);

        string root = ArchiveRoot;
        if (!Directory.Exists(root)) return new(true);
        if (IsReparsePoint(root)) return new(false);

        string expectedToken = token.ToString("D").ToLowerInvariant();
        string archivePath = Path.GetFullPath(Path.Combine(root, expectedToken + ".zip"));
        if (!string.Equals(Path.GetDirectoryName(archivePath), Path.GetFullPath(root), PathComparison))
            return new(false);
        string pendingPath = PendingArchivePath(expectedToken);
        foreach (string path in new[] { archivePath, pendingPath })
        {
            if (Directory.Exists(path) || (File.Exists(path) && IsReparsePoint(path))) return new(false);
        }
        File.Delete(archivePath);
        File.Delete(pendingPath);
        return new(!File.Exists(archivePath) && !File.Exists(pendingPath));
    }

    /// <summary>Opens only an atomically published archive; the HTTP caller supplies no path.</summary>
    public static FileStream? OpenCompletedArchive(string cleanupToken)
    {
        if (!Guid.TryParseExact(cleanupToken, "D", out Guid token) ||
            !string.Equals(cleanupToken, token.ToString("D"), StringComparison.Ordinal)) return null;
        string root = ArchiveRoot;
        string path = Path.GetFullPath(Path.Combine(root, cleanupToken + ".zip"));
        try
        {
            if (!string.Equals(Path.GetDirectoryName(path), root, PathComparison) ||
                !Directory.Exists(root) || IsReparsePoint(root) || !File.Exists(path) ||
                Directory.Exists(path) || IsReparsePoint(path)) return null;
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                HashBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string PendingArchivePath(string cleanupToken) =>
        Path.Combine(ArchiveRoot, ".pending-" + cleanupToken + ".zip");

    private static DeveloperProjectArchiveResult PrepareCore(
        DeveloperProjectArchiveRequest request,
        ArchiveOperation operation,
        Action<BackendOperationProgress>? report,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string initialProjectSHA = ComputeRevision(operation.ProjectPath, cancellationToken);
        if (!HashEquals(initialProjectSHA, request.ExpectedProjectSHA256))
            throw new SourceFileChangedException();
        operation.SourceProjectSHA256 = initialProjectSHA;

        if (IsSameOrWithin(operation.ProjectDirectory, ArchiveRoot) ||
            IsSameOrWithin(ArchiveRoot, operation.ProjectDirectory))
            throw new InvalidDataException("The selected project overlaps the private archive workspace.");
        EnsureArchiveDirectory();
        if (File.Exists(operation.ArchivePath) || Directory.Exists(operation.ArchivePath) ||
            File.Exists(operation.PendingArchivePath) || Directory.Exists(operation.PendingArchivePath))
            throw new IOException("The developer project archive identifier is already in use.");
        if (Directory.Exists(operation.StageDirectory) || File.Exists(operation.StageDirectory))
            throw new IOException("The developer project staging identifier is already in use.");

        bool completed = false;
        Project? stagedProject = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileSnapshot[] originalInventory = CaptureProjectFiles(operation.ProjectDirectory,
                cancellationToken);
            if (!originalInventory.Any(file => PathComparer.Equals(file.FullPath, operation.ProjectPath)) ||
                !HashEquals(originalInventory.Single(file => PathComparer.Equals(file.FullPath,
                    operation.ProjectPath)).SHA256, initialProjectSHA))
                throw new SourceFileChangedException();

            string sourceResultsDirectory = Project.GetDefaultResultDirectory(operation.ProjectDirectory);
            FileSnapshot[] includedSourceFiles = SelectArchiveFiles(originalInventory,
                sourceResultsDirectory, request.IncludeResults);
            if (includedSourceFiles.Length == 0 ||
                !includedSourceFiles.Any(file => PathComparer.Equals(file.FullPath, operation.ProjectPath)))
                throw new InvalidDataException("The source project file is not included in its project directory.");

            Directory.CreateDirectory(operation.StageDirectory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(operation.StageDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string stagedProjectDirectory = Path.Combine(operation.StageDirectory,
                Path.GetFileName(operation.ProjectDirectory));
            string stagedProjectPath = Path.Combine(stagedProjectDirectory,
                Path.GetFileName(operation.ProjectPath));
            CopyProjectSnapshot(includedSourceFiles, operation.ProjectDirectory,
                stagedProjectDirectory, report, cancellationToken);
            EnsureSameInventory(originalInventory, CaptureProjectFiles(operation.ProjectDirectory,
                cancellationToken));

            XElement[] originalConnections = ReadProjectConnections(stagedProjectPath);

            var stagedPaths = new HashSet<string>(includedSourceFiles.Select(file =>
                Path.GetFullPath(Path.Combine(stagedProjectDirectory,
                    Path.GetRelativePath(operation.ProjectDirectory, file.FullPath)))), PathComparer);
            stagedProject = new Project(stagedProjectPath,
                sourceFile => stagedPaths.Contains(Path.GetFullPath(sourceFile)));
            if (stagedProject.IsInMemory ||
                !PathComparer.Equals(Path.GetFullPath(stagedProject.Filename), stagedProjectPath) ||
                !PathComparer.Equals(Path.GetFullPath(stagedProject.ProjectDirectory), stagedProjectDirectory))
                throw new InvalidDataException("The source project could not be opened from its staged copy.");
            RestoreProjectConnections(stagedProject, originalConnections);

            FileSnapshot[]? inventoryBeforeZip = null;
            using var reporter = new ProgressRelay(cancellationToken, report,
                "Zipping " + stagedProject.ProjectDirectory, () =>
            {
                inventoryBeforeZip = CaptureIncludedFiles(stagedProject.ProjectDirectory,
                    stagedProject.ResultsDirectory, request.IncludeResults, cancellationToken);
            }, progressStart: 0.4, progressScale: 0.6);
            stagedProject.ZipFullProject(operation.PendingArchivePath, request.IncludeResults, reporter.Reporter);
            cancellationToken.ThrowIfCancellationRequested();
            FileSnapshot[] beforeZip = inventoryBeforeZip
                ?? throw new InvalidDataException("The source project archive did not report its staged file inventory.");
            string archivedProjectSHA = ComputeRevision(stagedProjectPath, cancellationToken);
            FileSnapshot[] afterZip = CaptureIncludedFiles(stagedProject.ProjectDirectory,
                stagedProject.ResultsDirectory, request.IncludeResults, cancellationToken);
            EnsureSameInventory(beforeZip, afterZip);
            int entryCount = VerifyArchive(operation.PendingArchivePath, afterZip, cancellationToken);
            FileSnapshot[] stableAfterVerification = CaptureIncludedFiles(stagedProject.ProjectDirectory,
                stagedProject.ResultsDirectory, request.IncludeResults, cancellationToken);
            EnsureSameInventory(afterZip, stableAfterVerification);

            string finalProjectSHA = ComputeRevision(stagedProjectPath, cancellationToken);
            if (!HashEquals(archivedProjectSHA, finalProjectSHA))
                throw new SourceFileChangedException();
            FileSnapshot projectEntry = afterZip.Single(file => PathComparer.Equals(
                file.FullPath, stagedProjectPath));
            if (!HashEquals(projectEntry.SHA256, archivedProjectSHA))
                throw new SourceFileChangedException();

            EnsureSameInventory(originalInventory, CaptureProjectFiles(operation.ProjectDirectory,
                cancellationToken));
            string inventorySHA = ComputeInventoryRevision(afterZip);
            completed = true;
            return new("completed", operation.CleanupToken, operation.ArchivePath,
                operation.ProjectPath, initialProjectSHA, archivedProjectSHA, inventorySHA,
                entryCount, request.IncludeResults);
        }
        finally
        {
            Exception? cleanupFailure = null;
            if (stagedProject is not null)
            {
                try { MapResultChartService.DisposeProject(stagedProject); }
                catch (Exception error) { cleanupFailure = error; }
            }
            try { TryDeleteStage(operation.StageDirectory, operation.CleanupToken); }
            catch (Exception error) { cleanupFailure ??= error; }
            try
            {
                if (cleanupFailure is not null)
                    throw new IOException("The staged project could not be fully removed.", cleanupFailure);
                if (completed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Atomic same-directory publication is visible to the separate HTTP process
                    // only after ZIP verification and staged-project disposal have succeeded.
                    File.Move(operation.PendingArchivePath, operation.ArchivePath, overwrite: false);
                }
            }
            finally
            {
                TryDeleteArchive(operation.PendingArchivePath);
            }
        }
    }

    private static ArchiveOperation CreateOperation(DeveloperProjectArchiveRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedProjectSHA256);
        if (!Path.IsPathFullyQualified(request.ProjectPath) ||
            !Path.GetExtension(request.ProjectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Open a saved project before uploading it.", nameof(request));
        if (!IsSHA256(request.ExpectedProjectSHA256))
            throw new ArgumentException("The source project revision is invalid.", nameof(request));

        string path = Path.GetFullPath(request.ProjectPath);
        if (!File.Exists(path)) throw new FileNotFoundException("The source project file was not found.", path);
        string projectDirectory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The source project folder is unavailable.", nameof(request));
        if (IsReparsePoint(path) || IsReparsePoint(projectDirectory))
            throw new InvalidDataException("The selected project contains a symbolic link that cannot be archived safely.");
        string token = Guid.NewGuid().ToString("D").ToLowerInvariant();
        string archivePath = Path.Combine(ArchiveRoot, token + ".zip");
        string stageDirectory = Path.Combine(ArchiveRoot, ".stage-" + token);
        return new(path, projectDirectory, token, archivePath, stageDirectory);
    }

    private static DeveloperProjectArchiveResult CancelledResult(
        DeveloperProjectArchiveRequest request,
        ArchiveOperation operation)
    {
        TryDeleteArchive(operation.PendingArchivePath);
        TryDeleteArchive(operation.ArchivePath);
        return new("cancelled", operation.CleanupToken, operation.ArchivePath,
            operation.ProjectPath, operation.SourceProjectSHA256 ?? request.ExpectedProjectSHA256,
            "", "", 0, request.IncludeResults);
    }

    private static FileSnapshot[] CaptureProjectFiles(string projectDirectory,
        CancellationToken cancellationToken)
    {
        string projectRoot = Path.GetFullPath(projectDirectory);
        string parentDirectory = Path.GetDirectoryName(projectRoot)
            ?? throw new IOException("The source project folder has no parent directory.");
        if (!Directory.Exists(projectRoot) || IsReparsePoint(projectRoot))
            throw new InvalidDataException("The selected project contains a symbolic link that cannot be archived safely.");

        var files = new List<FileSnapshot>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(projectRoot);
        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pendingDirectories.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The selected project contains a symbolic link that cannot be archived safely.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pendingDirectories.Push(entry);
                    continue;
                }

                string fullPath = Path.GetFullPath(entry);
                string entryPath = Path.GetRelativePath(parentDirectory, fullPath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (Path.IsPathRooted(entryPath) || entryPath.Split('/').Any(part => part is ".." or ""))
                    throw new InvalidDataException("A source file in the project resolves outside its directory.");
                files.Add(CaptureFile(fullPath, entryPath, cancellationToken));
            }
        }

        return files.OrderBy(file => file.EntryPath, StringComparer.Ordinal).ToArray();
    }

    private static FileSnapshot[] SelectArchiveFiles(FileSnapshot[] sourceFiles,
        string resultsDirectory, bool includeResults)
    {
        string canonicalResultsDirectory = Path.GetFullPath(resultsDirectory);
        return sourceFiles.Where(file =>
        {
            if (Path.GetFileName(file.FullPath) == ".lock") return false;
            string parent = Path.GetDirectoryName(file.FullPath)!;
            return includeResults || !parent.StartsWith(canonicalResultsDirectory,
                StringComparison.OrdinalIgnoreCase);
        }).ToArray();
    }

    private static void CopyProjectSnapshot(FileSnapshot[] sourceFiles, string sourceProjectDirectory,
        string stagedProjectDirectory, Action<BackendOperationProgress>? report,
        CancellationToken cancellationToken)
    {
        long totalBytes = sourceFiles.Sum(file => file.Length);
        long copiedTotal = 0;
        double denominator = Math.Max(1, totalBytes);
        foreach (FileSnapshot source in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(sourceProjectDirectory, source.FullPath);
            string destination = Path.GetFullPath(Path.Combine(stagedProjectDirectory, relativePath));
            if (!IsSameOrWithin(destination, stagedProjectDirectory))
                throw new InvalidDataException("A source file in the project resolves outside its directory.");
            string destinationDirectory = Path.GetDirectoryName(destination)
                ?? throw new IOException("A staged project file has no parent directory.");
            Directory.CreateDirectory(destinationDirectory);
            using var input = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                HashBufferSize, FileOptions.SequentialScan);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                HashBufferSize, FileOptions.SequentialScan);
            using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[HashBufferSize];
            long copied = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                hasher.AppendData(buffer, 0, read);
                copied += read;
                copiedTotal += read;
                report?.Invoke(new(Math.Clamp(0.4 * copiedTotal / denominator, 0, 0.4),
                    Message: "Preparing project archive…"));
            }
            output.Flush(flushToDisk: true);
            output.Dispose();
            string copiedSHA = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (copied != source.Length || !HashEquals(copiedSHA, source.SHA256))
                throw new SourceFileChangedException();
            File.SetLastWriteTimeUtc(destination, source.LastWriteTimeUtc);
        }
    }

    private static FileSnapshot[] CaptureIncludedFiles(string projectDirectory, string resultsDirectory,
        bool includeResults, CancellationToken cancellationToken) =>
        SelectArchiveFiles(CaptureProjectFiles(projectDirectory, cancellationToken),
            resultsDirectory, includeResults);

    private static XElement[] ReadProjectConnections(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.None);
        XElement? group = document.Root?.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Connections",
                StringComparison.OrdinalIgnoreCase));
        return group?.Elements().Where(element => string.Equals(element.Name.LocalName, "Connection",
                StringComparison.OrdinalIgnoreCase))
            .Select(element => new XElement(element)).ToArray() ?? Array.Empty<XElement>();
    }

    private static void RestoreProjectConnections(Project project, XElement[] originalConnections)
    {
        List<ExternalConnectionBase> stagedConnections = project.ConnectionSource.Connections;
        if (stagedConnections.Count != originalConnections.Length)
            throw new InvalidDataException("The staged project connections could not be matched to the source project.");

        for (int index = 0; index < stagedConnections.Count; index++)
        {
            XElement original = originalConnections[index];
            string originalName = original.Attribute("Name")?.Value ?? "";
            if (!string.Equals(stagedConnections[index].Name, originalName, StringComparison.Ordinal))
                throw new InvalidDataException("The staged project connections could not be matched to the source project.");
            stagedConnections[index].LoadXML(original);
        }
    }

    private static FileSnapshot CaptureFile(string path, string entryPath, CancellationToken cancellationToken)
    {
        var before = new FileInfo(path);
        long beforeLength = before.Length;
        DateTime beforeWriteTime = before.LastWriteTimeUtc;
        string revision = ComputeRevision(path, cancellationToken);
        var after = new FileInfo(path);
        if (!after.Exists || beforeLength != after.Length || beforeWriteTime != after.LastWriteTimeUtc)
            throw new SourceFileChangedException();
        return new(path, entryPath, after.Length, revision, after.LastWriteTimeUtc);
    }

    private static int VerifyArchive(string archivePath, FileSnapshot[] expected,
        CancellationToken cancellationToken)
    {
        var expectedByName = expected.ToDictionary(file => file.EntryPath, StringComparer.Ordinal);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(entry.FullName))
                throw new InvalidDataException("The upload archive does not match the current project files.");
            if (!expectedByName.TryGetValue(entry.FullName, out FileSnapshot? file) || file is null)
                throw new InvalidDataException("The upload archive does not match the current project files.");
            if (entry.Length != file.Length || !HashEquals(ComputeRevision(entry, cancellationToken), file.SHA256))
                throw new SourceFileChangedException();
        }
        if (seen.Count == 0 || seen.Count != expectedByName.Count ||
            expectedByName.Keys.Any(name => !seen.Contains(name)))
            throw new InvalidDataException("The upload archive does not match the current project files.");
        return seen.Count;
    }

    private static void EnsureSameInventory(FileSnapshot[] expected, FileSnapshot[] actual)
    {
        if (expected.Length != actual.Length) throw new SourceFileChangedException();
        for (int index = 0; index < expected.Length; index++)
        {
            if (!PathComparer.Equals(expected[index].FullPath, actual[index].FullPath) ||
                !string.Equals(expected[index].EntryPath, actual[index].EntryPath, StringComparison.Ordinal) ||
                expected[index].Length != actual[index].Length ||
                expected[index].LastWriteTimeUtc != actual[index].LastWriteTimeUtc ||
                !HashEquals(expected[index].SHA256, actual[index].SHA256))
                throw new SourceFileChangedException();
        }
    }

    private static string ComputeInventoryRevision(FileSnapshot[] files)
    {
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FileSnapshot file in files.OrderBy(file => file.EntryPath, StringComparer.Ordinal))
        {
            byte[] line = Encoding.UTF8.GetBytes($"{file.EntryPath}\0{file.Length}\0{file.SHA256}\n");
            hasher.AppendData(line);
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComputeRevision(string path, CancellationToken cancellationToken)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            HashBufferSize, FileOptions.SequentialScan);
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[HashBufferSize];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComputeRevision(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        using Stream input = entry.Open();
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[HashBufferSize];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool IsSHA256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool HashEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static void EnsureArchiveDirectory()
    {
        string root = ArchiveRoot;
        Directory.CreateDirectory(root);
        if (IsReparsePoint(root))
            throw new IOException("The private project archive folder is not safe to use.");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void TryDeleteArchive(string path)
    {
        string root = ArchiveRoot;
        if (!Directory.Exists(root) || IsReparsePoint(root)) return;
        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(root), PathComparison)) return;
        if (Directory.Exists(fullPath) || !File.Exists(fullPath)) return;
        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) == 0) File.Delete(fullPath);
    }

    private static void TryDeleteStage(string path, string cleanupToken)
    {
        if (!Directory.Exists(path)) return;
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetFullPath(ArchiveRoot);
        if (!string.Equals(Path.GetDirectoryName(fullPath), root, PathComparison) ||
            !string.Equals(Path.GetFileName(fullPath), ".stage-" + cleanupToken, StringComparison.Ordinal) ||
            IsReparsePoint(fullPath))
            throw new IOException("The owned project staging folder failed its cleanup check.");
        Directory.Delete(fullPath, recursive: true);
    }

    private static bool IsSameOrWithin(string path, string parent)
    {
        string fullPath = Path.GetFullPath(path);
        string fullParent = Path.GetFullPath(parent);
        if (PathComparer.Equals(fullPath, fullParent)) return true;
        string relative = Path.GetRelativePath(fullParent, fullPath);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string ArchiveRoot => Path.GetFullPath(Path.Combine(Path.GetTempPath(),
        "HECRAS2025-DeveloperArchives"));

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class ArchiveOperation(string projectPath, string projectDirectory,
        string cleanupToken, string archivePath, string stageDirectory)
    {
        public string ProjectPath { get; } = projectPath;
        public string ProjectDirectory { get; } = projectDirectory;
        public string CleanupToken { get; } = cleanupToken;
        public string ArchivePath { get; } = archivePath;
        public string PendingArchivePath { get; } = DeveloperProjectArchiveService.PendingArchivePath(cleanupToken);
        public string StageDirectory { get; } = stageDirectory;
        public string? SourceProjectSHA256 { get; set; }
    }

    private sealed record FileSnapshot(string FullPath, string EntryPath, long Length, string SHA256,
        DateTime LastWriteTimeUtc);

    private sealed class ProgressRelay : IDisposable
    {
        private readonly CancellationToken cancellationToken;
        private readonly Action<BackendOperationProgress>? report;
        private readonly string zipStartMessage;
        private readonly double progressStart;
        private readonly double progressScale;
        private bool didCaptureZipStart;

        public ProgressReporter Reporter { get; } = new();

        public ProgressRelay(CancellationToken cancellationToken, Action<BackendOperationProgress>? report,
            string zipStartMessage, Action zipStarting, double progressStart, double progressScale)
        {
            this.cancellationToken = cancellationToken;
            this.report = report;
            this.zipStartMessage = zipStartMessage;
            this.progressStart = progressStart;
            this.progressScale = progressScale;
            this.zipStarting = zipStarting;
            Reporter.ProgressReported += OnProgress;
            Reporter.MessageReported += OnMessage;
        }

        private readonly Action zipStarting;

        private void OnProgress(ProgressReporter sender, int percent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            report?.Invoke(new(Math.Clamp(progressStart + (percent / 100.0 * progressScale),
                progressStart, progressStart + progressScale),
                TaskName: sender.FullyQualifiedTaskName));
        }

        private void OnMessage(ProgressReporter sender, Utility.Logging.Message message)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!didCaptureZipStart && string.Equals(message.EvaluatedMessage, zipStartMessage,
                    StringComparison.Ordinal))
            {
                didCaptureZipStart = true;
                zipStarting();
            }
            report?.Invoke(new(Message: message.EvaluatedMessage,
                TaskName: sender.FullyQualifiedTaskName));
        }

        public void Dispose()
        {
            Reporter.ProgressReported -= OnProgress;
            Reporter.MessageReported -= OnMessage;
        }
    }
}
