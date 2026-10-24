using System.Security.Cryptography;
using Ras.Layers;
using Ras.Migration;
using Utility.Progress;

namespace Ras.Backend;

public sealed record ProjectMigrationInspectRequest(string SourcePath, string DestinationParentPath);
public sealed record ProjectMigrationInspection(bool CanExecute, string? ValidationMessage,
    string SourcePath, string? ProjectName, string? DestinationPath, string? Revision);
public sealed record ProjectMigrationRequest(string SourcePath, string DestinationPath, string Revision);
public sealed record ProjectMigrationResult(string SourcePath, string DestinationPath, string ProjectPath, string ProjectName,
    IReadOnlyList<string>? Messages = null);

/// <summary>
/// Uses the retained HEC-RAS v6-to-2025 model converter directly. The source
/// project is read in place and conversion is written to a new directory.
/// </summary>
public static class ProjectMigrationService
{
    public static ProjectMigrationInspection Inspect(ProjectMigrationInspectRequest request)
    {
        string sourcePath = request.SourcePath;
        string? projectName = null;
        string? revision = null;
        string? destinationPath = null;
        string? validation = null;
        try
        {
            var source = ReadSource(request.SourcePath);
            sourcePath = source.Path;
            projectName = source.ProjectName;
            revision = source.Revision;
            string parent = FullDirectory(request.DestinationParentPath);
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The selected project folder does not exist.");
            destinationPath = SuggestDestination(parent, Path.GetFileNameWithoutExtension(sourcePath));
            if (SameMigrationDirectory(destinationPath, Path.GetDirectoryName(sourcePath)!))
                throw new ArgumentException("Choose an output folder separate from the source project folder.");
        }
        catch (ArgumentException error) { validation = error.Message; }
        catch (IOException error) { validation = error.Message; }
        return new(validation == null, validation, sourcePath, projectName, destinationPath, revision);
    }

    public static ProjectMigrationResult Execute(ProjectMigrationRequest request, Action<BackendOperationProgress>? report = null)
    {
        var source = ReadSource(request.SourcePath);
        if (string.IsNullOrWhiteSpace(request.Revision) || !string.Equals(source.Revision, request.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string destination = FullDirectory(request.DestinationPath);
        string parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The selected project folder does not exist.");
        if (SameMigrationDirectory(destination, Path.GetDirectoryName(source.Path)!))
            throw new ArgumentException("Choose an output folder separate from the source project folder.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new ArgumentException("The migration output folder already exists. Inspect again to choose a new folder.");

        // Create only the new output folder. If conversion fails or is cancelled
        // by terminating this one-request backend process, keep any partial
        // output for diagnosis; the v6 source file is never removed or rewritten.
        Directory.CreateDirectory(destination);
        Ras.Layers.Project? migrated = null;
        var messages = new List<string>();
        var reportLock = new object();
        var reporter = new ProgressReporter();
        reporter.ProgressReported += (sender, progress) =>
        {
            double? fraction = progress is >= 0 and <= 100 ? progress / 100.0 : null;
            lock (reportLock)
                report?.Invoke(new BackendOperationProgress(Fraction: fraction, TaskName: sender.TaskName));
        };
        reporter.MessageReported += (sender, message) =>
        {
            string evaluatedMessage = message.EvaluatedMessage;
            lock (reportLock)
            {
                messages.Add(evaluatedMessage);
                report?.Invoke(new BackendOperationProgress(Message: evaluatedMessage, TaskName: sender.TaskName));
            }
        };
        try
        {
            migrated = ModelConverter.MigrateProject(source.Path, destination, MigrationParams.Default(), reporter);
            if (migrated == null) throw new InvalidDataException("The retained converter did not return a migrated project.");
            string[] projects = Directory.GetFiles(destination, "*.ras", SearchOption.TopDirectoryOnly);
            if (projects.Length != 1)
                throw new InvalidDataException($"Project migration produced {projects.Length} RAS project files in \"{destination}\" instead of one.");
            if (!File.Exists(projects[0])) throw new FileNotFoundException("The migrated RAS project file was not created.", projects[0]);
            if (!File.Exists(source.Path) || !string.Equals(ComputeRevision(source.Path), request.Revision, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            string[] finalMessages;
            lock (reportLock) finalMessages = messages.ToArray();
            return new(source.Path, destination, Path.GetFullPath(projects[0]), Path.GetFileNameWithoutExtension(projects[0]), finalMessages);
        }
        finally
        {
            if (migrated != null) Dispose(migrated);
        }
    }

    private static SourceInfo ReadSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute path to a HEC-RAS v6 .prj file.");
        string source = Path.GetFullPath(path);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected project file does not exist.", source);
        if (!Path.GetExtension(source).Equals(".prj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The selected file does not have the .prj extension.");

        string[] lines = File.ReadAllLines(source);
        // MigrationHelpers and Ras.Migration.Native.Project require the first
        // line to start with this exact key. The converter also reads line 4
        // for the legacy unit system, so reject shorter files before conversion
        // instead of reproducing the source's Substring/index exceptions.
        if (lines.Length == 0 || !lines[0].StartsWith("Proj Title", StringComparison.Ordinal))
            throw new ArgumentException("The file does not start with the expected key \"Proj Title\".");
        if (lines.Length < 4)
            throw new ArgumentException("The v6 project file is incomplete; the unit-system line is missing.");

        string projectName = Path.GetFileNameWithoutExtension(source);
        int equals = lines[0].IndexOf('=');
        if (equals >= 0 && !string.IsNullOrWhiteSpace(lines[0][(equals + 1)..]))
            projectName = lines[0][(equals + 1)..].Trim();
        string revision = ComputeRevision(source);
        return new(source, projectName, revision);
    }

    private static string ComputeRevision(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string SuggestDestination(string parent, string stem)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            string candidate = Path.Combine(parent, stem + "_" + Guid.NewGuid().ToString("N")[..8]);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not find an unused migration output folder.");
    }

    private static string FullDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute project folder path.");
        return Path.GetFullPath(path);
    }

    private static bool SameMigrationDirectory(string left, string right)
    {
        string canonicalLeft = CanonicalDirectoryPath(left);
        string canonicalRight = CanonicalDirectoryPath(right);
        return string.Equals(canonicalLeft, canonicalRight,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string CanonicalDirectoryPath(string path)
    {
        string fullPath = SourceFilePaths.Canonical(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows()) return fullPath;
        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The migration directory must be absolute.", nameof(path));
        string current = root;
        string[] segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            string candidate = Path.Combine(current, segment);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                string remainder = Path.Combine(segments[index..]);
                return Path.GetFullPath(Path.Combine(current, remainder));
            }

            string[] entries = Directory.Exists(current)
                ? Directory.EnumerateFileSystemEntries(current).Select(Path.GetFileName).OfType<string>().ToArray()
                : [];
            if (entries.Contains(segment, StringComparer.Ordinal))
            {
                current = Path.Combine(current, segment);
                continue;
            }

            string[] caseAliases = entries.Where(name => string.Equals(name, segment, StringComparison.OrdinalIgnoreCase)).ToArray();
            current = Path.Combine(current, caseAliases.Length == 1 ? caseAliases[0] : segment);
        }
        return Path.GetFullPath(current);
    }

    private static void Dispose(Ras.Layers.Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
            .Concat(project.Geometries).Concat(project.Terrains).Concat(project.BoundaryConditions)
            .Concat(project.NValues).Concat(project.ClassificationLayers)) layer.Dispose();
    }

    private sealed record SourceInfo(string Path, string ProjectName, string Revision);
}
