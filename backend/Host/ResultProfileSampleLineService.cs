using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Observables;
using Utility.Progress;
using Utility.Units;

namespace Ras.Backend;

/// <summary>
/// Runs the retained Result.ComputeFlowTimeSeriesAsync callback against an
/// explicit sample line. Candidate overlays are limited to the selected
/// Result's current-project ObservablePlan.NameMatchedResults group.
/// </summary>
public static class ResultProfileSampleLineService
{
    private const int MaximumSampleLinePoints = 10_000;

    public static ResultProfileSourcesResult Sources(ResultProfileSourcesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectPath = ValidateProject(request.ProjectPath, request.ProjectXml);
        string selectedPath = ValidateResultPath(request.ResultPath, request.Revision);
        Project project = LoadProject(projectPath, request.ProjectXml);
        try
        {
            Result selected = FindProjectResult(project, selectedPath);
            EnsureUnchanged(selectedPath, request.Revision);
            ResultProfileSourceChoice[] sources = MatchedResultObjects(project, selected)
                .Select(Describe).ToArray();
            EnsureUnchanged(selectedPath, request.Revision);
            foreach (ResultProfileSourceChoice source in sources)
                EnsureUnchanged(source.ResultPath, source.Revision);

            var (areaName, cellCount, faceCount) = FirstFlowArea(selected);
            return new ResultProfileSourcesResult(projectPath, selectedPath, request.Revision,
                selected.Plan?.Name ?? string.Empty, areaName, cellCount, faceCount, sources);
        }
        finally { DisposeProject(project); }
    }

    public static async Task<ResultProfileSampleLineResult> ComputeAsync(ResultProfileSampleLineRequest request,
        Action<BackendOperationProgress>? report = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectPath = ValidateProject(request.ProjectPath, request.ProjectXml);
        string selectedPath = ValidateResultPath(request.ResultPath, request.Revision);
        string mode = request.Mode switch
        {
            "Flow" => "Flow",
            "CumulativeVolume" => "CumulativeVolume",
            _ => throw new ArgumentException("Choose Flow or Cumulative Volume for the sample-line result.", nameof(request.Mode))
        };
        GeometryTopologyCoordinate[] points = ValidateSampleLine(request.SampleLine);
        ResultProfileSourceIdentity[] requestedSources = ValidateSources(request.Sources, selectedPath, request.Revision);

        Project project = LoadProject(projectPath, request.ProjectXml);
        try
        {
            Result selected = FindProjectResult(project, selectedPath);
            Result[] allowed = MatchedResultObjects(project, selected);
            Dictionary<string, Result> allowedByPath = allowed.ToDictionary(
                result => CanonicalResultPath(result.Filename), StringComparer.Ordinal);
            var selectedResults = new List<(Result Result, string Path, string Revision)>(requestedSources.Length);
            foreach (ResultProfileSourceIdentity source in requestedSources)
            {
                string path = ValidateResultPath(source.ResultPath, source.Revision);
                if (!allowedByPath.TryGetValue(path, out Result? result))
                    throw new ArgumentException("The selected overlay result is not available for this embedded plan.");
                EnsureUnchanged(path, source.Revision);
                selectedResults.Add((result, path, source.Revision));
            }

            var line = new Polyline();
            foreach (GeometryTopologyCoordinate point in points)
                line.Add(new Point(point.X, point.Y));

            var series = new ResultProfileSampleLineSeries[selectedResults.Count];
            for (int index = 0; index < selectedResults.Count; index++)
            {
                (Result result, string path, string revision) = selectedResults[index];
                EnsureUnchanged(path, revision);
                series[index] = await ComputeOneAsync(result, path, revision, line, mode, report);
                EnsureUnchanged(path, revision);
            }

            EnsureUnchanged(selectedPath, request.Revision);
            foreach (var source in selectedResults)
                EnsureUnchanged(source.Path, source.Revision);
            return new ResultProfileSampleLineResult(projectPath, selectedPath, request.Revision,
                mode, points, series);
        }
        finally { DisposeProject(project); }
    }

    internal static async Task<ResultProfileSampleLineSeries> ComputeOneAsync(Result result, string resultPath,
        string revision, Polyline line, string mode, Action<BackendOperationProgress>? report)
    {
        string resultName = result.Name;
        var firstArea = result.Geometry?.FlowAreaLayer?.FirstOrDefault();
        if (firstArea?.Mesh is null)
            return Unavailable(resultPath, revision, resultName, mode,
                "The result has no mesh in its first embedded 2D flow area.", result);

        try
        {
            // Keep the same callback used by DynamicTimeSeriesProfilePlot and
            // PolylineLayerBehaviors. Cumulative volume follows the source
            // RateTimeSeries integration, including its linear interpolation.
            ProgressReporter reporter = CreateReporter(report);
            (var operation, double[] flowValues) = await result.ComputeFlowTimeSeriesAsync(line, reporter);
            if (!operation.Result)
                return Unavailable(resultPath, revision, resultName, mode,
                    operation.GetConcatenatedMessages(), result);

            DateTime[] plotTimes = result.BaseOutputBlock?.Times?.ToArray() ?? [];
            double[] values;
            string quantity;
            string units;
            if (mode == "Flow")
            {
                values = flowValues;
                quantity = "Flow";
                units = DefaultUnits.FromDefault(result.EmbeddedUnitSystem).FlowUnits;
            }
            else
            {
                using var postProcessRead = new ResultPostProcessReadScope(result);
                var postProcessor = postProcessRead.Processor;
                DateTime[] flowTimes = postProcessor.Times?.ToArray() ?? [];
                if (flowTimes.Length != flowValues.Length)
                    throw new InvalidDataException("The source flow samples do not match the post-process timestamps.");
                values = new RateTimeSeries(flowTimes, flowValues, Time.Seconds,
                    ProfileInterpolationMethod.Linear).AccumulatedValues.ToArray();
                quantity = "Volume";
                units = DefaultUnits.FromDefault(result.EmbeddedUnitSystem).ShortVolumeUnits;
            }

            if (plotTimes.Length != values.Length)
                throw new InvalidDataException("The source sample-line values do not match the Result chart timestamps.");
            ResultProfileSampleLinePoint[] points = plotTimes.Select((time, index) =>
                new ResultProfileSampleLinePoint(index,
                    time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                    double.IsFinite(values[index]) ? values[index] : null)).ToArray();
            return new ResultProfileSampleLineSeries(resultPath, revision, resultName,
                mode, quantity, units, true, null, points);
        }
        catch (Exception error) when (error is not SourceFileChangedException)
        {
            return Unavailable(resultPath, revision, resultName, mode, error.Message, result);
        }
    }

    private static ProgressReporter CreateReporter(Action<BackendOperationProgress>? report)
    {
        var reporter = new ProgressReporter();
        if (report is null) return reporter;
        reporter.ProgressReported += (sender, progress) =>
        {
            double? fraction = progress is >= 0 and <= 100 ? progress / 100.0 : null;
            report(new BackendOperationProgress(Fraction: fraction, TaskName: sender.TaskName));
        };
        reporter.MessageReported += (sender, message) =>
            report(new BackendOperationProgress(Message: message.EvaluatedMessage, TaskName: sender.TaskName));
        return reporter;
    }

    private static ResultProfileSampleLineSeries Unavailable(string resultPath, string revision,
        string resultName, string mode, string? reason, Result result)
    {
        string quantity = mode == "Flow" ? "Flow" : "Volume";
        string units = mode == "Flow"
            ? DefaultUnits.FromDefault(result.EmbeddedUnitSystem).FlowUnits
            : DefaultUnits.FromDefault(result.EmbeddedUnitSystem).ShortVolumeUnits;
        return new ResultProfileSampleLineSeries(resultPath, revision, resultName, mode,
            quantity, units, false,
            string.IsNullOrWhiteSpace(reason) ? "The source could not compute this sample-line series." : reason,
            []);
    }

    private static Result[] MatchedResultObjects(Project project, Result selected)
    {
        var matched = new List<Result>();
        if (!string.IsNullOrWhiteSpace(selected.Plan?.Name))
        {
            var observable = new ObservableProject(project, Utility.SynchronizationContext.None);
            ObservablePlan? plan = observable.Plans.LastOrDefault(candidate =>
                string.Equals(candidate.Name, selected.Plan.Name, StringComparison.Ordinal));
            if (plan is not null) matched.AddRange(plan.NameMatchedResults.Select(item => item.Result));
        }
        if (!matched.Any(result => SourceFilePaths.SameSourceFile(result.Filename, selected.Filename)))
            matched.Add(selected);
        return matched.Where(result => !result.IsInMemory && !string.IsNullOrWhiteSpace(result.Filename))
            .GroupBy(result => CanonicalResultPath(result.Filename), StringComparer.Ordinal)
            .Select(group => group.First()).ToArray();
    }

    private static ResultProfileSourceChoice Describe(Result result)
    {
        string resultPath = CanonicalResultPath(result.Filename);
        EnsureFileExists(resultPath);
        string revision = Revision(resultPath);
        var (areaName, cellCount, faceCount) = FirstFlowArea(result);
        return new ResultProfileSourceChoice(resultPath, revision, result.Name,
            result.Plan?.Name ?? string.Empty, result.ProfileCount, areaName, cellCount, faceCount);
    }

    private static (string? Name, int Cells, int Faces) FirstFlowArea(Result result)
    {
        var area = result.Geometry?.FlowAreaLayer?.FirstOrDefault();
        var mesh = area?.Mesh;
        return (area?.Name, mesh?.CellCount ?? 0, mesh?.FaceCount ?? 0);
    }

    private static ResultProfileSourceIdentity[] ValidateSources(ResultProfileSourceIdentity[]? sources,
        string selectedPath, string selectedRevision)
    {
        if (sources is null || sources.Length is < 1 or > 32)
            throw new ArgumentException("Select between one and 32 related results for the sample-line chart.", nameof(sources));
        var normalized = new List<ResultProfileSourceIdentity>(sources.Length);
        foreach (ResultProfileSourceIdentity source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            string path = ValidateResultPath(source.ResultPath, source.Revision);
            if (normalized.Any(item => string.Equals(item.ResultPath, path, StringComparison.Ordinal)))
                throw new ArgumentException("The sample-line chart contains a duplicate result.", nameof(sources));
            normalized.Add(new ResultProfileSourceIdentity(path, source.Revision));
        }
        if (!normalized.Any(source => string.Equals(source.ResultPath, selectedPath, StringComparison.Ordinal)
            && string.Equals(source.Revision, selectedRevision, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Include the selected result in the sample-line chart.", nameof(sources));
        return normalized.ToArray();
    }

    private static GeometryTopologyCoordinate[] ValidateSampleLine(GeometryTopologyCoordinate[]? sampleLine)
    {
        if (sampleLine is null || sampleLine.Length is < 2 or > MaximumSampleLinePoints)
            throw new ArgumentException("Draw a sample line with two or more points.", nameof(sampleLine));
        if (sampleLine.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("Sample-line coordinates must be finite.", nameof(sampleLine));
        return sampleLine;
    }

    private static string ValidateProject(string projectPath, string projectXml)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Path.IsPathFullyQualified(projectPath))
            throw new ArgumentException("Open a saved project before computing a sample-line series.", nameof(projectPath));
        if (string.IsNullOrWhiteSpace(projectXml))
            throw new ArgumentException("The current project XML context is unavailable.", nameof(projectXml));
        // Keep the selected .ras location as the base for its relative paths.
        // Physical canonical paths are reserved for identity and hash checks.
        string normalized = Path.GetFullPath(projectPath);
        if (!File.Exists(normalized)) throw new FileNotFoundException("The current project file was not found.", normalized);
        return normalized;
    }

    private static string ValidateResultPath(string resultPath, string revision)
    {
        if (string.IsNullOrWhiteSpace(resultPath) || !Path.IsPathFullyQualified(resultPath))
            throw new ArgumentException("Choose an absolute result file path.", nameof(resultPath));
        if (string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("Reload the result to obtain its source revision.", nameof(revision));
        string canonical = SourceFilePaths.Canonical(resultPath);
        EnsureFileExists(canonical);
        EnsureUnchanged(canonical, revision);
        return canonical;
    }

    private static string CanonicalResultPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The current project contains a result without a saved file path.");
        return SourceFilePaths.Canonical(path);
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected result file was not found.", path);
    }

    private static Project LoadProject(string projectPath, string projectXml)
    {
        Project project = new(projectPath);
        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(projectXml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 8 * 1024 * 1024
            });
            project.ApplyEditorMetadata(XElement.Load(reader));
            return project;
        }
        catch
        {
            DisposeProject(project);
            throw;
        }
    }

    private static Result FindProjectResult(Project project, string resultPath)
    {
        Result[] matches = project.Results.Where(result => !result.IsInMemory
            && !string.IsNullOrWhiteSpace(result.Filename)
            && SourceFilePaths.SameSourceFile(result.Filename, resultPath)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ArgumentException("The selected result is not in the current project."),
            _ => throw new ArgumentException("The selected result has an ambiguous project file identity.")
        };
    }

    private static void EnsureUnchanged(string path, string revision)
    {
        if (!string.Equals(Revision(path), revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static string Revision(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void DisposeProject(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
            .Concat(project.Geometries).Concat(project.BoundaryConditions).Concat(project.Terrains)
            .Concat(project.NValues).Concat(project.ClassificationLayers)) layer.Dispose();
    }
}
