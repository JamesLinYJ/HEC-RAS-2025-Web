using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Geospatial.GDALAssist;
using Ras.Layers;
using Utility.Units;

namespace Ras.Backend;

public sealed record ProjectWorkspaceOpenRequest(string ProjectPath);

public sealed record ProjectWorkspaceCreateRequest(
    string ProjectTitle,
    string Description,
    string CreateDirectory,
    bool IsUSC,
    bool IsLocalProj,
    bool IsEPSG,
    bool IsWktProj,
    bool IsPRJFile,
    string ProjectionText,
    ProjectionRequest? ProjectionDefinition);

public sealed record ProjectWorkspaceSaveRequest(
    string ProjectPath,
    string? ProjectXml,
    string ProjectSessionID,
    string Revision,
    string? ProjectXmlRevision = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ProjectWorkspaceResponse(bool Success, string ProjectPath, string ProjectXml, string Revision,
    CoreLayerRequestContext? CoreLayerContext = null);

/// <summary>Uses the retained Ras.Layers.Project implementation for project lifecycle operations.</summary>
public static class ProjectWorkspaceService
{
    private sealed record ProjectFileSnapshot(string Path, string Xml, string Revision);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProjectWriteLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly TimeSpan ProjectWriteLockTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Coordinates writes to one .ras project within this process and across worker processes on
    /// the same host. Lock files are intentionally persistent: deleting and recreating a lock file
    /// can let two processes lock different file identities for the same project.
    /// </summary>
    internal static IDisposable AcquireProjectWriteLock(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        string canonicalPath = CanonicalPath(ResolveProjectFile(projectPath));
        string processKey = OperatingSystem.IsWindows() ? canonicalPath.ToUpperInvariant() : canonicalPath;
        SemaphoreSlim processLock = ProjectWriteLocks.GetOrAdd(canonicalPath,
            static _ => new SemaphoreSlim(1, 1));
        if (!processLock.Wait(ProjectWriteLockTimeout))
            throw new TimeoutException("Timed out waiting for the project write lock.");

        try
        {
            string lockDirectory = Path.Combine(Path.GetTempPath(), "hecras-project-write-locks-v1");
            Directory.CreateDirectory(lockDirectory);
            string lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(processKey))) + ".lock";
            string lockPath = Path.Combine(lockDirectory, lockName);
            Stopwatch elapsed = Stopwatch.StartNew();
            IOException? lastLockError = null;
            while (elapsed.Elapsed < ProjectWriteLockTimeout)
            {
                try
                {
                    FileStream crossProcessLock = new(lockPath, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                    return new ProjectWriteLockLease(processLock, crossProcessLock);
                }
                catch (IOException exception)
                {
                    lastLockError = exception;
                    Thread.Sleep(50);
                }
            }

            throw new TimeoutException("Timed out waiting for another worker to finish writing this project.",
                lastLockError);
        }
        catch
        {
            processLock.Release();
            throw;
        }
    }

    private sealed class ProjectWriteLockLease(SemaphoreSlim processLock, FileStream crossProcessLock) : IDisposable
    {
        private FileStream? _crossProcessLock = crossProcessLock;
        private SemaphoreSlim? _processLock = processLock;

        public void Dispose()
        {
            FileStream? fileLock = Interlocked.Exchange(ref _crossProcessLock, null);
            SemaphoreSlim? localLock = Interlocked.Exchange(ref _processLock, null);
            try
            {
                fileLock?.Dispose();
            }
            finally
            {
                localLock?.Release();
            }
        }
    }

    public static ProjectWorkspaceResponse Open(ProjectWorkspaceOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectFilePath = ResolveProjectFile(request.ProjectPath);
        ProjectFileSnapshot before = ReadStableProjectFile(projectFilePath);
        Project project = new(projectFilePath);
        try
        {
            string loadedPath = CanonicalPath(project.Filename);
            if (!PathEquals(loadedPath, before.Path))
                throw new InvalidDataException("The Project model resolved a different project file than the requested path.");
            ProjectFileSnapshot after = ReadStableProjectFile(loadedPath);
            if (!StringComparer.Ordinal.Equals(before.Revision, after.Revision))
                throw new IOException("The project file changed while it was being opened. Retry opening it.");
            return new ProjectWorkspaceResponse(true, after.Path, after.Xml, after.Revision);
        }
        finally
        {
            DisposeProjectResources(project);
        }
    }

    public static ProjectWorkspaceResponse Create(ProjectWorkspaceCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string title = ValidateTitle(request.ProjectTitle);
        string parentDirectory = ValidateCreateDirectory(request.CreateDirectory);
        if (request.Description is null || Encoding.UTF8.GetByteCount(request.Description) > 1024 * 1024)
            throw new ArgumentException("The project description is too large.", nameof(request));

        string destinationDirectory = Path.GetFullPath(Path.Combine(parentDirectory, title));
        string destinationProjectPath = Path.GetFullPath(Path.Combine(destinationDirectory, title + Project.Extension));
        if (!IsContained(parentDirectory, destinationDirectory))
            throw new ArgumentException("The project title resolves outside the selected parent directory.", nameof(request));

        ProjectionDefinition projection = ResolveProjection(request);
        string stagingRoot = Path.Combine(parentDirectory, ".hecras-project-create-" + Guid.NewGuid().ToString("N") + ".partial");
        string stagedProjectDirectory = Path.Combine(stagingRoot, title);
        string stagedProjectPath = Path.Combine(stagedProjectDirectory, title + Project.Extension);
        Directory.CreateDirectory(stagingRoot);
        try
        {
            Directory.CreateDirectory(stagedProjectDirectory);
            Project project = Project.CreateNew(stagedProjectPath, projection);
            try
            {
                project.Description = request.Description;
                project.Save();
            }
            finally
            {
                DisposeProjectResources(project);
            }

            // Reopen with the retained project constructor before publishing the staged folder.
            _ = Open(new ProjectWorkspaceOpenRequest(stagedProjectPath));
            PublishWithoutOverwrite(stagedProjectDirectory, destinationDirectory);

            // Validate and capture the exact published file identity, not the staging path.
            return Open(new ProjectWorkspaceOpenRequest(destinationProjectPath));
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                try { Directory.Delete(stagingRoot, recursive: true); }
                catch (IOException) { /* Keep the successful project; staging cleanup is best effort. */ }
                catch (UnauthorizedAccessException) { /* Keep the successful project; staging cleanup is best effort. */ }
            }
        }
    }

    public static ProjectWorkspaceResponse Save(ProjectWorkspaceSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectSessionID))
            throw new ArgumentException("A project session ID is required.", nameof(request));
        if (!IsSHA256(request.Revision))
            throw new ArgumentException("A valid project source revision is required to save.", nameof(request));

        string projectFilePath = ResolveProjectFile(request.ProjectPath);
        using IDisposable projectWriteLock = AcquireProjectWriteLock(projectFilePath);
        ProjectFileSnapshot before = ReadStableProjectFile(projectFilePath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(before.Revision, request.Revision))
            throw new IOException("The project file changed since it was opened. Reload it before saving.");

        Project project = new(projectFilePath);
        try
        {
            ProjectFileSnapshot afterLoad = ReadStableProjectFile(projectFilePath);
            if (!StringComparer.OrdinalIgnoreCase.Equals(afterLoad.Revision, request.Revision))
                throw new IOException("The project file changed while it was being loaded. Reload it before saving.");

            // An external-source session capability is part of the save owner.
            // Validate its project/session identity, every captured source hash,
            // and the XML snapshot before the retained Project writer touches
            // the .ras file. OpenSessionProject is disposed immediately; its
            // attached references must remain session-only and must not become
            // project-owned assets during Project.Save().
            if (request.CoreLayerContext is { } currentContext)
            {
                if (string.IsNullOrWhiteSpace(request.ProjectXml) ||
                    !IsSHA256(request.ProjectXmlRevision) ||
                    !string.Equals(currentContext.ProjectSessionID, request.ProjectSessionID, StringComparison.Ordinal) ||
                    !string.Equals(currentContext.ProjectRevision, request.Revision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();

                Project validationProject = CoreLayerImportService.OpenSessionProject(projectFilePath,
                    request.ProjectXml, request.ProjectSessionID, request.Revision,
                    request.ProjectXmlRevision, currentContext);
                DisposeProjectResources(validationProject);
            }

            // ProjectXml is a frontend cache for other project-aware operations. It is deliberately
            // not written back here; retained Project.Load/Save owns project serialization.
            project.Save();
        }
        finally
        {
            DisposeProjectResources(project);
        }

        ProjectFileSnapshot saved = ReadStableProjectFile(projectFilePath);
        CoreLayerRequestContext? rebasedContext = request.CoreLayerContext is { } sourceContext
            ? CoreLayerImportService.RebaseContext(sourceContext, saved.Revision, saved.Path, saved.Revision)
            : null;
        if (rebasedContext is not null)
            CoreLayerImportService.ValidateContext(rebasedContext, saved.Path);
        return new ProjectWorkspaceResponse(true, saved.Path, saved.Xml, saved.Revision, rebasedContext);
    }

    private static ProjectionDefinition ResolveProjection(ProjectWorkspaceCreateRequest request)
    {
        int selectedModes = (request.IsLocalProj ? 1 : 0) + (request.IsEPSG ? 1 : 0)
            + (request.IsWktProj ? 1 : 0) + (request.IsPRJFile ? 1 : 0);
        if (selectedModes != 1)
            throw new ArgumentException("Select exactly one project projection mode.", nameof(request));

        if (request.IsLocalProj)
            return ProjectionDefinition.Local(request.IsUSC ? UnitSystem.USCustomary : UnitSystem.SI);

        string text = request.ProjectionText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("The projection definition is empty.", nameof(request));
        if (Encoding.UTF8.GetByteCount(text) > 4 * 1024 * 1024)
            throw new ArgumentException("The projection definition is too large.", nameof(request));

        if (request.IsPRJFile)
        {
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "hecras-prj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                string prjPath = Path.Combine(temporaryDirectory, "source.prj");
                File.WriteAllText(prjPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                ProjectionEvaluation evaluation = ProjectionService.Evaluate(new ProjectionRequest("ESRI", prjPath));
                if (!evaluation.IsValid || string.IsNullOrWhiteSpace(evaluation.Wkt))
                    throw new InvalidOperationException(evaluation.Error ?? "The ESRI PRJ definition is invalid.");
                return new ProjectionDefinition(ProjectionType.WKT, evaluation.Wkt);
            }
            finally
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }

        string type = request.IsEPSG
            ? "EPSG"
            : text.TrimStart().StartsWith("+proj", StringComparison.Ordinal) ? "Proj" : "WKT";
        ProjectionEvaluation result = ProjectionService.Evaluate(new ProjectionRequest(type,
            type == "EPSG" ? text.Trim() : text));
        if (!result.IsValid)
            throw new InvalidOperationException(result.Error ?? "The project projection definition is invalid.");
        ProjectionType normalizedType = ProjectionDefinition.TryParseProjectionType(result.NormalizedType);
        if (normalizedType == ProjectionType.Unknown)
            throw new InvalidDataException("The projection service returned an unknown normalized definition type.");
        return new ProjectionDefinition(normalizedType, result.NormalizedValue);
    }

    private static string ValidateTitle(string value)
    {
        string title = value?.Trim() ?? string.Empty;
        if (title.Length < 3 || title.Length > 120 || title is "." or ".." ||
            title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            title.Any(character => character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(character)))
            throw new ArgumentException("Project title must contain 3–120 valid filename characters.", nameof(value));
        return title;
    }

    private static string ValidateCreateDirectory(string value)
    {
        string path = value?.Trim() ?? string.Empty;
        if (path.Length == 0 || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Enter an absolute project parent directory on the C# server host.", nameof(value));
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException("The selected server project parent directory does not exist.");
        return fullPath;
    }

    private static string ResolveProjectFile(string value)
    {
        string path = value?.Trim() ?? string.Empty;
        if (path.Length == 0 || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Enter an absolute project path on the C# server host.", nameof(value));
        string fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            string[] rasFiles = Directory.EnumerateFiles(fullPath, "*", SearchOption.TopDirectoryOnly)
                .Where(file => string.Equals(Path.GetExtension(file), Project.Extension, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (rasFiles.Length == 0)
                throw new FileNotFoundException("The selected directory contains no .ras project file.", fullPath);
            if (rasFiles.Length != 1)
                throw new InvalidDataException("The selected directory contains multiple .ras project files; select one project file.");
            fullPath = Path.GetFullPath(rasFiles[0]);
        }
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The project file was not found on the C# server host.", fullPath);
        if (!string.Equals(Path.GetExtension(fullPath), Project.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The selected file is not a .ras project.", nameof(value));
        return fullPath;
    }

    private static ProjectFileSnapshot ReadStableProjectFile(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            (string? canonicalBefore, string? revisionBefore) = CatalogTreeService.TryReadStableRevision(path);
            if (canonicalBefore is null || revisionBefore is null)
                throw new IOException("The project file could not be read as a stable source.");
            string xml;
            try { xml = File.ReadAllText(path); }
            catch (IOException) when (attempt < 2) { continue; }
            (string? canonicalAfter, string? revisionAfter) = CatalogTreeService.TryReadStableRevision(path);
            if (canonicalAfter is not null && revisionAfter is not null && PathEquals(canonicalBefore, canonicalAfter)
                && StringComparer.Ordinal.Equals(revisionBefore, revisionAfter))
                return new ProjectFileSnapshot(canonicalAfter, xml, revisionAfter);
        }
        throw new IOException("The project file changed while it was being read. Retry the operation.");
    }

    private static void PublishWithoutOverwrite(string stagedProjectDirectory, string destinationDirectory)
    {
        if (File.Exists(destinationDirectory))
            throw new IOException("A file already exists at the requested project directory.");
        if (Directory.Exists(destinationDirectory))
        {
            if (Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
                throw new IOException("The requested project directory is not empty; refusing to overwrite it.");
            Directory.Delete(destinationDirectory, recursive: false);
        }
        Directory.Move(stagedProjectDirectory, destinationDirectory);
    }

    private static bool IsContained(string parent, string child)
    {
        string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(child).StartsWith(prefix, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string CanonicalPath(string path)
    {
        (string? canonical, _) = CatalogTreeService.TryReadStableRevision(path);
        return canonical ?? Path.GetFullPath(path);
    }

    private static bool PathEquals(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsSHA256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void DisposeProjectResources(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
            .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
            .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results)
            result.Dispose();
    }
}
