using System.Globalization;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Observables;
using Utility.Files;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private static readonly string[] BoundaryLineSeriesPalette =
    ["FF0000FF", "FFFF0000", "FF008000", "FFFFA500", "FF800080", "FF008080", "FFA52A2A", "FFFF00FF"];

    /// <summary>
    /// Computes original BoundaryConditionLine Flow/Stage datasets from an
    /// exact slot-zero Result owner. The source HDF remains read-only; source
    /// Result methods may create their documented post-process cache sidecar.
    /// </summary>
    public static async Task<MapBoundaryLineTimeSeriesResult> LoadMapBoundaryLineTimeSeriesAsync(
        MapBoundaryLineTimeSeriesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMapBoundaryLineTimeSeriesRequest(request);

        string projectPath = SourceFilePaths.Canonical(request.Map.ProjectPath);
        MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(
            request.Map, projectPath);
        // Refuse before MapResultChartService opens a Result HDF. An active
        // plan writer owns every path in this canonical set, including the
        // callback Result itself.
        if (readLease?.IsProtected(request.ResultOwner.SelectedResultPath) == true)
            throw new SourceFileChangedException();

        var boundaryRequest = new MapBoundaryGeometryRequest(request.Map, request.BoundaryNode,
            request.ExpectedBoundaryRevision, request.BoundarySourceState,
            request.ExpectedBoundaryContextToken, request.ResultOwner);
        MapBoundaryGeometrySnapshot boundarySnapshot = await LoadMapBoundaryGeometryAsync(boundaryRequest);
        if (boundarySnapshot.ResultOwner is not { } boundaryOwner ||
            !SameBoundaryResultOwner(boundaryOwner, request.ResultOwner) ||
            !string.Equals(boundarySnapshot.ContextToken, request.ExpectedBoundaryContextToken, StringComparison.Ordinal) ||
            !string.Equals(boundarySnapshot.BoundarySourceState, request.BoundarySourceState, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, request.ResultOwner.SelectionOverrides, request.ResultOwner.ContextToken);
        if (!string.Equals(context.Sources.ContextToken, request.ResultOwner.ContextToken, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        ResolvedMapResultNode selected = context.ResultContexts.SingleOrDefault(candidate =>
            candidate.Selections.Any(selection =>
                selection.SelectionIndex == request.ResultOwner.SelectionIndex &&
                SameBoundaryLocator(selection.Node, request.ResultOwner.ResultNode)) &&
            SourceFilePaths.SameSourceFile(candidate.ResultPath, request.ResultOwner.SelectedResultPath) &&
            string.Equals(candidate.Revision, request.ResultOwner.SelectedResultRevision,
                StringComparison.OrdinalIgnoreCase)) ?? throw new SourceFileChangedException();
        if (readLease?.IsProtected(selected.ResultPath) == true)
            throw new SourceFileChangedException();
        BoundaryCondition? boundary = selected.Result.BoundaryConditions;
        if (boundary is null || !boundary.IsResult ||
            !SourceFilePaths.SameSourceFile(selected.ResultPath, boundarySnapshot.BoundaryPath) ||
            !string.Equals(selected.Revision, boundarySnapshot.BoundaryRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        string sourcePath = SourceFilePaths.Canonical(selected.ResultPath);
        MapResultChartService.EnsureUnchanged(sourcePath, selected.Revision);
        context.EnsureUnchanged();

        BoundaryLineData[] sourceEditorLines;
        var observableProject = new ObservableProject(context.Project, Utility.SynchronizationContext.None);
        var observableResult = new ObservableResult(observableProject, selected.Result);
        ObservableBoundaryCondition observableBoundary = observableResult.BoundaryCondition;
        var sourceEditor = new BoundaryEditorService(boundary, observableBoundary, sourcePath,
            selected.Revision, request.BoundarySourceState);
        sourceEditorLines = sourceEditor.Snapshot().Lines;
        if (sourceEditorLines.Length != boundary.BoundaryConditionLines.Features.Count ||
            sourceEditorLines.Length != boundarySnapshot.Lines.Length)
            throw new SourceFileChangedException();

        MapBoundaryLineData[] selectedLines = request.LineIDs.Select(id =>
        {
            MapBoundaryLineData[] matches = boundarySnapshot.Lines.Where(line => line.Id == id).ToArray();
            if (matches.Length != 1 || matches[0].SourceOrdinal < 0 ||
                matches[0].SourceOrdinal >= sourceEditorLines.Length || sourceEditorLines[matches[0].SourceOrdinal].Id != id)
                throw new SourceFileChangedException();
            return matches[0];
        }).ToArray();

        var datasets = new List<MapBoundaryLineTimeSeriesDataset>();
        foreach (MapBoundaryLineData selectedLine in selectedLines)
        {
            BoundaryConditionLine line = boundary.BoundaryConditionLines.Features[selectedLine.SourceOrdinal];
            var lineObserver = new ObservableBoundaryConditionLine(observableBoundary, line);
            double[] computed;
            IReadOnlyList<DateTime> computedTimes;
            Utility.Logging.OperationResult computation = request.Variable == "Flow"
                ? boundary.TryGetBCLineFlowTimeSeries(line, out computed, out computedTimes)
                : boundary.TryGetBCLineStageTimeSeries(line, out computed, out computedTimes);
            if ((bool)computation && computed is { Length: > 0 } && computedTimes is not null)
            {
                if (computed.Length != computedTimes.Count)
                    throw new InvalidDataException("The source Boundary Condition line result series has mismatched times and values.");
                if (computedTimes.Count > 0)
                    datasets.Add(CreateBoundaryLineDataset(selectedLine, line.Name + " (Simulated)", "Simulated",
                        BoundaryLineSeriesPalette[datasets.Count % BoundaryLineSeriesPalette.Length], 2.5,
                        computedTimes, computed, ignoreFlowSentinel: true));
            }

            int payloadOrdinal = 0;
            foreach (ObservablePayload payload in lineObserver.Payloads)
            {
                int currentPayloadOrdinal = payloadOrdinal++;
                if (!string.Equals(payload.Variable, request.Variable, StringComparison.Ordinal) ||
                    payload is not ObservableTimeSeriesPayload series || series.Count == 0)
                    continue;
                double[] values = series.Values.ToArray();
                DateTime[] times = series.TimeSeriesDefinition.GetAbsoluteTimes(series.Count,
                    lineObserver.ActualStartTime);
                if (values.Length != times.Length)
                    throw new InvalidDataException("The source Boundary Condition payload has mismatched times and values.");
                datasets.Add(CreateBoundaryLineDataset(selectedLine,
                    line.Name + " — " + (payload.Name ?? string.Empty), "Payload",
                    BoundaryLineSeriesPalette[datasets.Count % BoundaryLineSeriesPalette.Length], 2.0,
                    times, values, ignoreFlowSentinel: false,
                    payloadID: sourceEditorLines[selectedLine.SourceOrdinal].Payloads[currentPayloadOrdinal].Id));
            }
        }

        MapResultChartService.EnsureUnchanged(sourcePath, selected.Revision);
        context.EnsureUnchanged();
        MapBoundaryGeometrySnapshot finalBoundary = await LoadMapBoundaryGeometryAsync(boundaryRequest);
        if (!string.Equals(finalBoundary.ContextToken, boundarySnapshot.ContextToken, StringComparison.Ordinal) ||
            !string.Equals(finalBoundary.BoundarySourceState, boundarySnapshot.BoundarySourceState, StringComparison.Ordinal) ||
            !SameBoundaryResultOwner(finalBoundary.ResultOwner, request.ResultOwner) ||
            !SameBoundaryLineIdentities(finalBoundary.Lines, boundarySnapshot.Lines))
            throw new SourceFileChangedException();

        return new MapBoundaryLineTimeSeriesResult(context.ProjectPath, context.ProjectRevision,
            context.ProjectXmlSHA, context.VisualizationRevision, request.Map.MapID,
            request.Map.MapIndex, request.Map.MapName, request.Map.MapType,
            context.Sources.ContextToken, boundarySnapshot.ContextToken,
            request.BoundaryNode, sourcePath, selected.Revision, boundaryOwner,
            request.Variable, datasets.ToArray());
    }

    private static MapBoundaryLineTimeSeriesDataset CreateBoundaryLineDataset(
        MapBoundaryLineData line, string name, string kind, string color, double width,
        IReadOnlyList<DateTime> times, IReadOnlyList<double> values, bool ignoreFlowSentinel,
        string? payloadID = null)
    {
        var points = new MapBoundaryLineTimeSeriesPoint[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            double value = values[index];
            double? plotted = double.IsFinite(value) && (!ignoreFlowSentinel || value != -9999.0)
                ? value : null;
            points[index] = new(times[index].ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture), plotted);
        }
        return new MapBoundaryLineTimeSeriesDataset(
            line.Id + "/" + (payloadID is null ? "simulated" : "payload/" + payloadID),
            name, kind, line.SourceOrdinal, color, width, points);
    }

    private static void ValidateMapBoundaryLineTimeSeriesRequest(MapBoundaryLineTimeSeriesRequest request)
    {
        if (request.Map is null || request.BoundaryNode is null || request.ResultOwner is null ||
            request.BoundaryNode.NodeType != "BoundaryCondition" ||
            string.IsNullOrWhiteSpace(request.ExpectedBoundaryRevision) ||
            request.ExpectedBoundaryRevision.Length != 64 ||
            request.ExpectedBoundaryRevision.Any(character => !Uri.IsHexDigit(character)) ||
            string.IsNullOrWhiteSpace(request.BoundarySourceState) ||
            string.IsNullOrWhiteSpace(request.ExpectedBoundaryContextToken) ||
            request.ExpectedBoundaryContextToken.Length != 64 ||
            request.ExpectedBoundaryContextToken.Any(character => !Uri.IsHexDigit(character)) ||
            request.ResultOwner.SelectionIndex != 0 ||
            request.ResultOwner.ResultNode.NodeType is not ("Result" or "MultiResult") ||
            request.LineIDs is null || request.LineIDs.Length is 0 or > 10_000 ||
            request.LineIDs.Any(string.IsNullOrWhiteSpace) ||
            request.LineIDs.Distinct(StringComparer.Ordinal).Count() != request.LineIDs.Length ||
            request.Variable is not ("Flow" or "Stage"))
            throw new ArgumentException("A result-owned Boundary Condition line and Flow or Stage chart selection are required.");
    }

    private static bool SameBoundaryResultOwner(MapResultOwnerIdentity? actual,
        MapResultGeometryOwner expected) => actual is { } value &&
        SameBoundaryLocator(value.ResultNode, expected.ResultNode) &&
        string.Equals(value.ContextToken, expected.ContextToken, StringComparison.Ordinal) &&
        SameBoundarySelectionOverrides(value.SelectionOverrides, expected.SelectionOverrides) &&
        value.SelectionIndex == expected.SelectionIndex &&
        SourceFilePaths.SameSourceFile(value.SelectedResultPath, expected.SelectedResultPath) &&
        string.Equals(value.SelectedResultRevision, expected.SelectedResultRevision, StringComparison.OrdinalIgnoreCase);

    private static bool SameBoundarySelectionOverrides(MapResultSelectionOverride[]? actual,
        MapResultSelectionOverride[]? expected)
    {
        MapResultSelectionOverride[] left = actual ?? [];
        MapResultSelectionOverride[] right = expected ?? [];
        return left.Length == right.Length && left.Zip(right).All(pair =>
            SameBoundaryLocator(pair.First.Node, pair.Second.Node) &&
            pair.First.SelectionIndex == pair.Second.SelectionIndex &&
            string.Equals(pair.First.Mode, pair.Second.Mode, StringComparison.Ordinal) &&
            string.Equals(pair.First.ResultPath, pair.Second.ResultPath, StringComparison.Ordinal) &&
            string.Equals(pair.First.ResultRevision, pair.Second.ResultRevision, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameBoundaryLineIdentities(MapBoundaryLineData[] actual,
        MapBoundaryLineData[] expected) => actual.Length == expected.Length &&
        actual.Zip(expected).All(pair => pair.First.Id == pair.Second.Id &&
            pair.First.SourceOrdinal == pair.Second.SourceOrdinal);

    private static bool SameBoundaryLocator(CatalogTreeNodeLocator left,
        CatalogTreeNodeLocator right) => BoundaryLocatorKey(left) == BoundaryLocatorKey(right);

    private static string BoundaryLocatorKey(CatalogTreeNodeLocator locator) => string.Join('\u001f',
        locator.RootName, locator.NodeType, locator.NodeName,
        locator.NodePath is null ? string.Empty : string.Join('.', locator.NodePath),
        locator.AssetID ?? string.Empty, locator.MapType ?? string.Empty,
        locator.Filename ?? string.Empty, locator.FilenameRelative ?? string.Empty,
        locator.ParentGeometryAssetID ?? string.Empty,
        locator.ParentGeometryNodePath is null ? string.Empty : string.Join('.', locator.ParentGeometryNodePath),
        locator.ParentGeometryNodeName ?? string.Empty,
        locator.AssociationKind ?? string.Empty, locator.AssociationAssetID ?? string.Empty,
        locator.ParentPlanAssetID ?? string.Empty,
        locator.ParentPlanNodePath is null ? string.Empty : string.Join('.', locator.ParentPlanNodePath),
        locator.ParentPlanNodeName ?? string.Empty,
        locator.ParentResultNodeType ?? string.Empty,
        locator.ParentResultAssetID ?? string.Empty,
        locator.ParentResultNodePath is null ? string.Empty : string.Join('.', locator.ParentResultNodePath),
        locator.ParentResultNodeName ?? string.Empty,
        locator.ParentResultRevision ?? string.Empty);
}
