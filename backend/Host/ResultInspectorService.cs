using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using Geospatial;
using H5Assist;
using Ras.Layers;
using Ras.Layers.Results;
using Ras.Output;
using Utility.Logging;

namespace Ras.Backend;

public sealed record ResultInspectorRequest(string ProjectPath, string ProjectXml, string ResultPath,
    string? Revision = null, CoreLayerRequestContext? CoreLayerContext = null);

/// <summary>
/// Read-only authorization for moving a result into the operating system Trash.
/// The backend never deletes or saves files; the client must show confirmation
/// and re-run this validation immediately before the native Trash operation.
/// </summary>
public sealed record ResultDeletePreparationRequest(string ProjectPath, string ProjectXml,
    string ResultPath, string Revision);

public sealed record ResultDeletePreparation(string ProjectPath, string ResultPath,
    string Name, string Revision);

public sealed record ResultModelIdentity(string Name, string? Description, string? ShortID,
    string? Filename, string? DateCreated, string? DateLastModified, bool EmbeddedInResult);

public sealed record ResultComputeSummary(string StartTime, string EndTime, bool Success,
    string? FailureReason, string? Solver, string? SoftwareVersion, string TotalDuration,
    double TotalDurationSeconds, string SolverInitDuration, double SolverInitDurationSeconds,
    string OutputDuration, double OutputDurationSeconds, string? ComputeLog);

public sealed record ResultOutputBlock(string Name, int ProfileCount, string[] ProfileNames,
    string[] Times);
public sealed record ResultInspectorLogDatum(string Key, string? Value);
public sealed record ResultInspectorLayerError(string Code, string Severity, string EvaluatedMessage,
    ResultInspectorLogDatum[] LogData);

/// <summary>
/// Read-only metadata from the Result object embedded in the selected HDF5 file.
/// Plan, geometry and boundary identities are deliberately sourced from the Result's
/// embedded model snapshot, not re-resolved from a same-name project asset.
/// </summary>
public sealed record ResultInspectorSnapshot(string ResultPath, string Revision, string Name,
    bool HasComputed, bool NeedsMigration, int ProfileCount, ResultComputeSummary? ComputeSummary,
    ResultModelIdentity? Plan, ResultModelIdentity? Geometry,
    ResultModelIdentity? BoundaryCondition, string? ExactTerrainName,
    string? AssociatedTerrainName, string? OverrideTerrainName, bool ShowAutoDetectedTerrain,
    string[] TerrainChoices, ResultOutputBlock[] OutputBlocks, ResultInspectorLayerError[] LayerErrors);

/// <summary>
/// Inspects a selected result through the renamed source Result/Project APIs. This
/// service never mutates or saves either the HDF5 result or the project XML.
/// </summary>
public static class ResultInspectorService
{
    public static ResultDeletePreparation PrepareDelete(ResultDeletePreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
            throw new ArgumentException("Open a saved project before deleting a result.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.");
        if (string.IsNullOrWhiteSpace(request.ResultPath) || !Path.IsPathFullyQualified(request.ResultPath))
            throw new ArgumentException("Select a saved result file.");
        if (string.IsNullOrWhiteSpace(request.Revision))
            throw new ArgumentException("Refresh the result before deleting it.");

        string projectPath = SourceFilePaths.Canonical(request.ProjectPath);
        string resultPath = SourceFilePaths.Canonical(request.ResultPath);
        if (!File.Exists(resultPath))
            throw new FileNotFoundException("The selected result file could not be found.", resultPath);

        string revision = Revision(resultPath);
        if (!string.Equals(request.Revision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        Project project = new(projectPath);
        try
        {
            project.ApplyEditorMetadata(XElement.Parse(request.ProjectXml, LoadOptions.None));
            var matchingResults = project.Results.Where(candidate =>
                !candidate.IsInMemory && !string.IsNullOrWhiteSpace(candidate.Filename)
                && SourceFilePaths.SameSourceFile(candidate.Filename, resultPath)).ToArray();
            if (matchingResults.Length != 1)
                throw new ArgumentException("The selected file is not exactly one saved result in the current project.");

            Result result = matchingResults[0];
            if (ProjectLayerPaths(project).Any(path => SourceFilePaths.SameSourceFile(path, resultPath)))
                throw new ArgumentException("The selected file is also referenced as a project model and cannot be deleted as a result.");

            // A model may be shared or referenced by the Result. Only the exact
            // Result file is eligible; no associated terrain or embedded model
            // file is added to this preparation.
            if (!string.Equals(Revision(resultPath), revision, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            return new ResultDeletePreparation(projectPath, resultPath, result.Name, revision);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    public static ResultInspectorSnapshot Inspect(ResultInspectorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
            throw new ArgumentException("Open a saved project before inspecting a result.");
        if (string.IsNullOrWhiteSpace(request.ResultPath) || !Path.IsPathFullyQualified(request.ResultPath))
            throw new ArgumentException("Select a saved result file.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.");

        string projectPath = SourceFilePaths.Canonical(request.ProjectPath);
        string resultPath = SourceFilePaths.Canonical(request.ResultPath);
        if (!File.Exists(resultPath)) throw new FileNotFoundException("The selected result file could not be found.", resultPath);

        string revision = Revision(resultPath);
        if (request.Revision != null && !string.Equals(request.Revision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        Project project = CoreLayerImportService.OpenProject(projectPath, request.ProjectXml,
            request.CoreLayerContext);
        try
        {
            Result result = project.Results.FirstOrDefault(candidate =>
                SourceFilePaths.SameSourceFile(candidate.Filename, resultPath))
                ?? throw new ArgumentException("The selected HDF5 file is not a result in the current project.");

            ResultAssociation association = project.GetResultAssociation(result);
            // The source metadata reader can instantiate a default object for
            // an absent group. Only stored summary metadata represents a run;
            // a failed run with no profiles can still have a real summary.
            bool hasStoredSummary;
            using (var reader = new H5Reader(resultPath))
                hasStoredSummary = reader.ExistsAsGroup("/Results/" + Result.ComputeSummaryGroupName);
            var compute = hasStoredSummary ? result.ComputeSummary : null;
            var blocks = (result.OutputBlocks ?? []).Select(block => new ResultOutputBlock(
                block.Name,
                block.ProfileCount,
                block.ProfileNames?.ToArray() ?? [],
                block.Times?.Select(FormatDate).ToArray() ?? []))
                .ToArray();
            var layerErrors = result.LayerErrors?.Select(message => new ResultInspectorLayerError(
                message.MessageType?.Code ?? string.Empty,
                (message.MessageType?.DefaultSeverity ?? MessageSeverity.Information).ToString(),
                message.EvaluatedMessage,
                message.LogData.Select(pair => new ResultInspectorLogDatum(pair.Key, FormatLogValue(pair.Value))).ToArray()))
                .ToArray() ?? [];
            var snapshot = new ResultInspectorSnapshot(
                resultPath,
                revision,
                result.Name,
                result.HasComputed,
                result.NeedsMigration,
                result.ProfileCount,
                compute == null ? null : new ResultComputeSummary(
                    FormatDate(compute.StartTime),
                    FormatDate(compute.EndTime),
                    compute.Success,
                    EmptyToNull(compute.FailureReason),
                    EmptyToNull(compute.Solver),
                    EmptyToNull(compute.SoftwareVersion),
                    FormatDuration(compute.TotalDuration),
                    compute.TotalDuration.TotalSeconds,
                    FormatDuration(compute.SolverInitDuration),
                    compute.SolverInitDuration.TotalSeconds,
                    FormatDuration(compute.OutputDuration),
                    compute.OutputDuration.TotalSeconds,
                    EmptyToNull(compute.ComputeLog)),
                Identity(result.Plan, embedded: result.Plan?.IsResult == true),
                Identity(result.Geometry, embedded: true),
                Identity(result.BoundaryConditions, embedded: true),
                project.GetExactTerrainFor(result)?.Name,
                result.AssociatedTerrain?.Name,
                EmptyToNull(association.OverrideTerrainName),
                !association.HasOverride,
                project.Terrains.Select(terrain => terrain.Name).ToArray(),
                blocks,
                layerErrors);

            if (!string.Equals(Revision(resultPath), revision, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            return snapshot;
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static ResultModelIdentity? Identity(CoreLayerBase? layer, bool embedded)
    {
        if (layer == null) return null;
        string? shortID = layer is Plan plan ? EmptyToNull(plan.ShortID) : null;
        return new(layer.Name, EmptyToNull(layer.Description), shortID,
            string.IsNullOrWhiteSpace(layer.Filename) ? null : SourceFilePaths.Canonical(layer.Filename),
            EmptyToNull(layer.DateCreated), EmptyToNull(layer.DateLastModified), embedded);
    }

    private static string FormatDate(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string FormatDuration(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string? FormatLogValue(object? value) => value switch
    {
        null => null,
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static string Revision(string path)
    {
        using FileStream stream = File.OpenRead(path);
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

    private static IEnumerable<string> ProjectLayerPaths(Project project) =>
        project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries).Concat(project.Terrains)
            .Concat(project.BoundaryConditions).Concat(project.NValues).Concat(project.ClassificationLayers)
            .Select(layer => layer.Filename)
            .Where(path => !string.IsNullOrWhiteSpace(path));

}
