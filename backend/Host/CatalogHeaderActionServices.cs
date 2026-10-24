using System.Security.Cryptography;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Layers.Results;

namespace Ras.Backend;

public sealed record ProjectProjectionExportRequest(
    string ProjectPath,
    string ProjectXml,
    string ExpectedProjectRevision,
    string DestinationPath);

public sealed record ProjectProjectionExportReceipt(
    string ProjectPath,
    string ProjectRevision,
    string OutputPath);

/// <summary>
/// Exports the captured Project projection through the retained Projection
/// implementation. The user-selected destination is never overwritten.
/// </summary>
public static class ProjectProjectionExportService
{
    public static ProjectProjectionExportReceipt Export(ProjectProjectionExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectPath = ValidateExistingPath(request.ProjectPath, "Open a saved project before exporting its projection.");
        ValidateRevision(request.ExpectedProjectRevision, "Reload the project before exporting its projection.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.");
        string destination = NormalizeDestination(request.DestinationPath);

        string initialProjectRevision = Revision(projectPath);
        if (!string.Equals(initialProjectRevision, request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Project project = new(projectPath);
        string? temporaryPath = null;
        string? publishedPath = null;
        try
        {
            project.ApplyEditorMetadata(XElement.Parse(request.ProjectXml, LoadOptions.None));
            if (project.IsInMemory)
                throw new ArgumentException("Cannot export a projection for an in-memory project.");
            if (project.Projection is null)
                throw new InvalidOperationException("The project has no projection to export.");
            if (!string.Equals(Revision(projectPath), initialProjectRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            string outputDirectory = Path.GetDirectoryName(destination)
                ?? throw new ArgumentException("Choose a valid projection export destination.");
            Directory.CreateDirectory(outputDirectory);
            string exportPath = Path.Combine(outputDirectory, ".hecras-projection-" + Guid.NewGuid().ToString("N") + ".partial");
            temporaryPath = exportPath;
            project.Projection.ExportEsri(exportPath);
            if (!File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
                throw new IOException("Projection export did not produce a non-empty ESRI projection file.");
            if (!string.Equals(Revision(projectPath), initialProjectRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            // File.Move without overwrite provides an atomic no-clobber publish.
            // If another process claims the requested name meanwhile, keep the
            // source behavior by selecting the next available Projection N.prj.
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string candidate = UniqueDestination(destination, attempt);
                try
                {
                    File.Move(exportPath, candidate);
                    temporaryPath = null;
                    publishedPath = candidate;
                    break;
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    // Retry with the next unique name; no existing file is replaced.
                }
            }
            if (publishedPath is null)
                throw new IOException("Error - do you really have 1000 projection files with the same name in the same directory?");
            return new(projectPath, initialProjectRevision, publishedPath);
        }
        finally
        {
            try { DisposeProject(project); } catch { }
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static string NormalizeDestination(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose a valid projection export destination.");
        string fullPath = Path.GetFullPath(path);
        if (string.IsNullOrEmpty(Path.GetExtension(fullPath))) fullPath += ".prj";
        if (!Path.GetExtension(fullPath).Equals(".prj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The projection export destination must use the .prj extension.");
        return fullPath;
    }

    private static string UniqueDestination(string requestedPath, int attempt)
    {
        if (attempt == 0) return requestedPath;
        string directory = Path.GetDirectoryName(requestedPath)!;
        string name = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        return Path.Combine(directory, $"{name} ({attempt}){extension}");
    }

    private static string ValidateExistingPath(string path, string message)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new ArgumentException(message);
        return SourceFilePaths.Canonical(path);
    }

    private static void ValidateRevision(string revision, string message)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.Length != 64 || !revision.All(Uri.IsHexDigit))
            throw new ArgumentException(message);
    }

    private static string Revision(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void DisposeProject(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                     .Concat(project.Geometries).Concat(project.Terrains)
                     .Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
    }
}

public sealed record IncompleteResultDeletePreparationRequest(
    string ProjectPath,
    string ProjectXml,
    string ProjectRevision,
    string ResultPath,
    string ResultRevision);

public sealed record IncompleteResultDeletePreparation(
    string ProjectPath,
    string ProjectRevision,
    string ResultPath,
    string ResultName,
    string ResultRevision);

/// <summary>
/// Revalidates one exact incomplete Result immediately before the client moves
/// that source HDF file to Trash. This service never deletes or edits files.
/// </summary>
public static class ResultRemovalService
{
    public static IncompleteResultDeletePreparation PrepareIncomplete(
        IncompleteResultDeletePreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectPath = ValidatePath(request.ProjectPath, "Open a saved project before removing incomplete results.");
        string resultPath = ValidatePath(request.ResultPath, "Select a saved Result file before removing incomplete results.");
        ValidateRevision(request.ProjectRevision, "Reload the project before removing incomplete results.");
        ValidateRevision(request.ResultRevision, "Refresh the Result before removing incomplete results.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.");

        string projectRevision = Revision(projectPath);
        string resultRevision = Revision(resultPath);
        if (!string.Equals(projectRevision, request.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resultRevision, request.ResultRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Project project = new(projectPath);
        try
        {
            project.ApplyEditorMetadata(XElement.Parse(request.ProjectXml, LoadOptions.None));
            Result[] matches = project.Results.Where(result => !result.IsInMemory &&
                !string.IsNullOrWhiteSpace(result.Filename) &&
                SourceFilePaths.SameSourceFile(result.Filename, resultPath)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected file is not exactly one saved Result in the current project.");

            Result result = matches[0];
            if (result.IsComputing())
                throw new InvalidOperationException("A Result that is still computing cannot be removed.");
            if (result.ComputeSummary?.Success == true)
                throw new InvalidOperationException("The selected Result completed successfully and cannot be removed as incomplete.");
            if (ProjectLayerPaths(project).Any(path => SourceFilePaths.SameSourceFile(path, resultPath)))
                throw new ArgumentException("The selected file is also referenced as a project model and cannot be removed as a Result.");

            if (!string.Equals(Revision(projectPath), projectRevision, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Revision(resultPath), resultRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            return new(projectPath, projectRevision, resultPath, result.Name, resultRevision);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static string ValidatePath(string path, string message)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new ArgumentException(message);
        return SourceFilePaths.Canonical(path);
    }

    private static void ValidateRevision(string revision, string message)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.Length != 64 || !revision.All(Uri.IsHexDigit))
            throw new ArgumentException(message);
    }

    private static string Revision(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static IEnumerable<string> ProjectLayerPaths(Project project) =>
        project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries).Concat(project.Terrains)
            .Concat(project.BoundaryConditions).Concat(project.NValues).Concat(project.ClassificationLayers)
            .Select(layer => layer.Filename)
            .Where(path => !string.IsNullOrWhiteSpace(path));

    private static void DisposeProject(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                     .Concat(project.Geometries).Concat(project.Terrains)
                     .Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
    }
}
