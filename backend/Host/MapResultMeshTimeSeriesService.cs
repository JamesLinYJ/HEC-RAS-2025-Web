using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geospatial.Vectors.Meshes;
using Ras.Hydraulics;
using Ras.Layers.Results;
using Ras.Output;
using Ras.Observables;
using Utility.Logging;
using Utility.Progress;
using Utility.Units;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    public static MapResultMeshTimeSeriesInspectResult InspectMapResultMeshTimeSeries(
        MapResultMeshTimeSeriesInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MapResultMeshTimeSeriesOwner owner = ValidateMapResultMeshOwner(request.Owner);
        MapTreeRequest map = MapWithSession(owner);
        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(map,
            owner.SelectionOverrides, owner.ContextToken);
        ValidateCoreLayerSourceContext(map.CoreLayerContext, context.ProjectPath);

        MapResultMeshOriginSnapshot origin = ResolveOrigin(context, owner);
        MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(map,
            SourceFilePaths.Canonical(map.ProjectPath));
        string[] matchingResultNames = MatchingResultNames(context, origin.FlowAreas, readLease);
        context.EnsureUnchanged();
        origin.EnsureUnchanged();

        return new MapResultMeshTimeSeriesInspectResult(
            origin.FlowAreas.Any(area => area.HasMesh) ? "ready" : "noMesh",
            origin.FlowAreas.Any(area => area.HasMesh) ? null : "The selected source has no mesh in this Flow Area layer.",
            owner.ProjectSessionID,
            context.ProjectPath,
            context.ProjectRevision,
            context.ProjectXmlSHA,
            context.VisualizationRevision,
            map.CoreLayerContext?.IdentityToken ?? string.Empty,
            context.Sources.MapID,
            context.Sources.MapIndex,
            context.Sources.MapName,
            context.Sources.MapType,
            context.Sources.ContextToken,
            origin.Kind,
            origin.Path,
            origin.Revision,
            origin.MeshDraftSHA256,
            origin.TopologyDraftSHA256,
            origin.ResultNode,
            origin.SelectionIndex,
            matchingResultNames,
            origin.FlowAreas,
            CountComputingChoices(context.Sources));
    }

    internal static async Task<MapResultMeshTimeSeriesResult> LoadMapResultMeshTimeSeriesDuplexAsync(
        MapResultMeshTimeSeriesExecuteRequest request,
        Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(duplexControls);
        MapResultMeshTimeSeriesOwner owner = ValidateMapResultMeshOwner(request.Owner);
        if (string.IsNullOrWhiteSpace(request.OperationID))
            throw new ArgumentException("A mesh time-series operation identity is required.");
        if (request.ElementKind is not ("Cell" or "Face") || request.ElementIndex < 0)
            throw new ArgumentException("Choose an exact mesh cell or face index.");

        MapTreeRequest map = MapWithSession(owner);
        MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(map,
            SourceFilePaths.Canonical(map.ProjectPath));
        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(map,
            owner.SelectionOverrides, owner.ContextToken);
        ValidateCoreLayerSourceContext(map.CoreLayerContext, context.ProjectPath);
        MapResultMeshOriginSnapshot origin = ResolveOrigin(context, owner);
        MapResultMeshFlowAreaChoice flowArea = RequireFlowArea(origin.FlowAreas,
            request.FlowAreaIndex, request.FlowAreaID, request.ExpectedFlowAreaName);
        if (!flowArea.HasMesh) throw new InvalidDataException("The selected Flow Area has no mesh.");
        int originElementCount = request.ElementKind == "Cell" ? flowArea.CellCount : flowArea.FaceCount;
        if (request.ElementIndex >= originElementCount)
            throw new ArgumentOutOfRangeException(nameof(request.ElementIndex),
                "The selected mesh element index is outside the exact source mesh.");

        OutputVariable variable = request.ElementKind == "Cell" ? OutputVariables.Depth : OutputVariables.Velocity;
        string units = TimeSeriesUnits(context.Project.Units, request.ElementKind);
        int skippedComputing = CountComputingChoices(context.Sources);
        ResolvedMapResultNode[] selectedResults = context.ResultContexts
            .Where(result => result.SelectionIndices.Contains(0) &&
                result.Selections.Any(selection => selection.SelectionIndex == 0))
            .DistinctBy(result => (SourceFilePaths.Canonical(result.ResultPath), result.Revision),
                ResultSourceIdentityComparer.Instance)
            .OrderBy(result => result.ResultPath, StringComparer.Ordinal)
            .ToArray();
        var matching = new List<(ResolvedMapResultNode Context, Ras.Layers.Geometry Geometry,
            FlowArea FlowArea, int MeshHash)>();
        foreach (ResolvedMapResultNode selected in selectedResults)
        {
            if (selected.Result.IsComputing())
            {
                skippedComputing++;
                continue;
            }
            if (readLease?.IsProtected(selected.ResultPath) == true) continue;
            MapResultChartService.EnsureUnchanged(selected.ResultPath, selected.Revision);
            Ras.Layers.Geometry? resultGeometry = selected.Result.Geometry;
            if (resultGeometry is null || request.FlowAreaIndex >= resultGeometry.FlowAreaLayer.Features.Count)
            {
                MapResultChartService.EnsureUnchanged(selected.ResultPath, selected.Revision);
                continue;
            }
            FlowArea candidateFlowArea = resultGeometry.FlowAreaLayer.Features[request.FlowAreaIndex];
            var candidateSignature = candidateFlowArea.PropertyTableSignature;
            bool matches = candidateFlowArea.Mesh is not null &&
                candidateFlowArea.Mesh.CellCount == flowArea.CellCount &&
                candidateFlowArea.Mesh.FaceCount == flowArea.FaceCount &&
                candidateSignature is not null && candidateSignature.MeshHash == flowArea.MeshHash;
            MapResultChartService.EnsureUnchanged(selected.ResultPath, selected.Revision);
            if (!matches) continue;
            if (candidateSignature is null) throw new InvalidOperationException("The Result mesh signature disappeared during capture.");
            int candidateHash = candidateSignature.MeshHash;
            matching.Add((selected, resultGeometry, candidateFlowArea, candidateHash));
        }

        if (matching.Count == 0)
        {
            context.EnsureUnchanged();
            origin.EnsureUnchanged();
            return Result("noMatchingMesh",
                skippedComputing > 0
                    ? "No active completed Result uses this exact Flow Area mesh. Results still computing were skipped."
                    : "No active Result uses this exact Flow Area mesh.", []);
        }

        var progress = new ProgressReporter();
        progress.ProgressReported += (sender, percent) => report?.Invoke(new BackendOperationProgress(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        progress.MessageReported += (sender, message) => report?.Invoke(new BackendOperationProgress(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));
        var outputSeries = new List<MapResultMeshTimeSeriesSeries>(matching.Count);
        int cancelReceived = 0;
        using var stopControls = new CancellationTokenSource();
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls
                    .ReadAllAsync(stopControls.Token).ConfigureAwait(false))
                {
                    if (frame.Control.TryGetProperty("type", out var typeValue) &&
                        typeValue.GetString() == "cancel" &&
                        Interlocked.Exchange(ref cancelReceived, 1) == 0)
                        progress.RequestCancel();
                }
            }
            catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
        }, stopControls.Token);

        try
        {
            report?.Invoke(new BackendOperationProgress(TaskName: "Mesh Element Result Time Series"));
            for (int index = 0; index < matching.Count; index++)
            {
                if (progress.CancelRequested) break;
                (ResolvedMapResultNode resultContext, _, FlowArea candidateFlowArea, int candidateHash) = matching[index];
                MapResultChartService.EnsureUnchanged(resultContext.ResultPath, resultContext.Revision);
                ProgressReporter resultProgress = progress.WithProgressModifier(
                    (int)Math.Round(index * 100.0 / matching.Count), 1f / matching.Count);
                resultProgress.ReportMessage("Reading " + resultContext.ActiveNode.Name);
                using var postProcessRead = new ResultPostProcessReadScope(resultContext.Result);
                PostProcessor postProcessor = postProcessRead.Processor;
                await postProcessor.EnsureDefaultDatasetsAsync(resultProgress).ConfigureAwait(false);
                if (progress.CancelRequested) break;
                MapResultChartService.EnsureUnchanged(resultContext.ResultPath, resultContext.Revision);
                int elementCount = request.ElementKind == "Cell"
                    ? candidateFlowArea.Mesh.CellCount : candidateFlowArea.Mesh.FaceCount;
                if (request.ElementIndex >= elementCount)
                    throw new SourceFileChangedException();
                float[] values = postProcessor.ReadTransposedDataset<float>(variable, request.ElementIndex);
                DateTime[] times = postProcessor.Times.ToArray();
                if (values.Length != times.Length)
                    throw new InvalidDataException("The Result time axis and mesh element values have different lengths.");
                string[] timeStrings = times.Select(time => time.ToString("O", CultureInfo.InvariantCulture)).ToArray();
                MapResultSelectionSource[] resultOwners = resultContext.Selections
                    .Where(selection => selection.SelectionIndex == 0)
                    .ToArray();
                outputSeries.Add(new MapResultMeshTimeSeriesSeries(
                    resultOwners.Select(selection => selection.Node).ToArray(),
                    resultOwners.Select(selection => selection.SelectionIndex).ToArray(),
                    SourceFilePaths.Canonical(resultContext.ResultPath),
                    resultContext.Revision.ToUpperInvariant(),
                    resultContext.ActiveNode.Name,
                    request.FlowAreaIndex,
                    candidateFlowArea.Name,
                    candidateHash,
                    variable.Name,
                    timeStrings,
                    values.Select(value => (double)value).ToArray()));
                MapResultChartService.EnsureUnchanged(resultContext.ResultPath, resultContext.Revision);
                resultProgress.ReportProgress(100);
            }
        }
        catch (OperationCanceledException) when (progress.CancelRequested)
        {
            // Keep cancellation distinct from Result read/transposition failure.
        }
        finally
        {
            stopControls.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        bool cancelled = progress.CancelRequested || Volatile.Read(ref cancelReceived) != 0;
        context.EnsureUnchanged();
        origin.EnsureUnchanged();
        foreach (ResolvedMapResultNode selected in selectedResults)
            if (readLease?.IsProtected(selected.ResultPath) != true)
                MapResultChartService.EnsureUnchanged(selected.ResultPath, selected.Revision);

        if (cancelled)
            return Result("cancelled", "The mesh element time-series request was cancelled.", []);
        return Result("ready", null, outputSeries.ToArray());

        MapResultMeshTimeSeriesResult Result(string state, string? message,
            MapResultMeshTimeSeriesSeries[] series) => new(
                request.OperationID,
                state,
                message,
                owner.ProjectSessionID,
                context.ProjectPath,
                context.ProjectRevision,
                context.ProjectXmlSHA,
                context.VisualizationRevision,
                map.CoreLayerContext?.IdentityToken ?? string.Empty,
                context.Sources.MapID,
                context.Sources.MapIndex,
                context.Sources.MapName,
                context.Sources.MapType,
                context.Sources.ContextToken,
                origin.Kind,
                origin.Path,
                origin.Revision,
                origin.MeshDraftSHA256,
                origin.TopologyDraftSHA256,
                origin.ResultNode,
                origin.SelectionIndex,
                request.FlowAreaIndex,
                request.FlowAreaID,
                request.ExpectedFlowAreaName,
                flowArea.MeshHash,
                request.ElementKind,
                request.ElementIndex,
                variable.Name,
                units,
                series,
                skippedComputing);
    }

    private static MapResultMeshTimeSeriesOwner ValidateMapResultMeshOwner(
        MapResultMeshTimeSeriesOwner? owner)
    {
        if (owner?.Map is null || string.IsNullOrWhiteSpace(owner.ProjectSessionID) ||
            !Guid.TryParse(owner.ProjectSessionID, out Guid sessionID) || sessionID == Guid.Empty ||
            string.IsNullOrWhiteSpace(owner.ContextToken) ||
            (owner.GeometryOrigin is null) == (owner.ResultOrigin is null))
            throw new ArgumentException("Choose one exact Geometry or Result origin and a retained map Result snapshot.");
        if (!string.IsNullOrWhiteSpace(owner.Map.ProjectSessionID))
        {
            if (!Guid.TryParse(owner.Map.ProjectSessionID, out Guid mapSessionID) || mapSessionID != sessionID)
                throw new SourceFileChangedException();
        }
        if (owner.GeometryOrigin is { } geometry)
        {
            if (!string.Equals(geometry.ProjectSessionID, owner.ProjectSessionID, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(geometry.AssetID) || geometry.Model is null ||
                geometry.Model.Kind != "Geometry" || string.IsNullOrWhiteSpace(geometry.Revision) ||
                string.IsNullOrWhiteSpace(geometry.DraftState) ||
                !MatchesSha(geometry.MeshDraftSHA256, MeshResultTimeSeriesHash(geometry.DraftState)) ||
                !MatchesSha(geometry.TopologyDraftSHA256, MeshResultTimeSeriesHash(geometry.TopologyDraftState ?? string.Empty)))
                throw new ArgumentException("The Geometry origin does not contain its exact current child draft.");
        }
        if (owner.ResultOrigin is { } result)
        {
            if (result.ResultNode is null || result.ResultNode.NodeType is not ("Result" or "MultiResult") ||
                result.SelectionIndex != 0 || result.ResultNode.NodePath is null ||
                string.IsNullOrWhiteSpace(result.SelectedResultPath) ||
                string.IsNullOrWhiteSpace(result.SelectedResultRevision))
                throw new ArgumentException("Cross-Result plots require the exact current Result slot zero.");
        }
        return owner;
    }

    private static MapTreeRequest MapWithSession(MapResultMeshTimeSeriesOwner owner) =>
        owner.Map with { ProjectSessionID = owner.ProjectSessionID };

    private static MapResultMeshOriginSnapshot ResolveOrigin(ResolvedMapResultContext context,
        MapResultMeshTimeSeriesOwner owner)
    {
        if (owner.GeometryOrigin is { } geometryOrigin)
        {
            GeometryMeshModelRequest model = geometryOrigin.Model;
            if (!SourceFilePaths.SameSourceFile(model.ProjectPath ?? string.Empty, context.ProjectPath) ||
                !string.Equals(MeshResultTimeSeriesHash(model.ProjectXml ?? string.Empty), context.ProjectXmlSHA,
                    StringComparison.OrdinalIgnoreCase) ||
                model.CoreLayerContext?.IdentityToken != context.Map.CoreLayerContext?.IdentityToken)
                throw new SourceFileChangedException();
            string geometryPath = SourceFilePaths.Canonical(model.AssetPath);
            string revision = Revision(geometryPath);
            if (!string.Equals(revision, geometryOrigin.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            using var modelContext = new ModelContext(ToModelRequest(model));
            ApplyGeometryTopologyDraft(modelContext, geometryPath, revision, geometryOrigin.TopologyDraftState);
            GeometryMeshDraftState state = ReadGeometryMeshDraft(geometryOrigin.DraftState, geometryPath, revision);
            EnsureTopologyFingerprint(state, GeometryMeshTopologyFingerprint(modelContext));
            _ = ApplyGeometryMeshMutationsAsync(modelContext, geometryPath, revision, state,
                replayPropertyTableComputations: false).GetAwaiter().GetResult();
            var (geometry, _) = GeometryMeshTopologyFor(modelContext);
            MapResultMeshFlowAreaChoice[] flowAreas = geometry.FlowAreaLayer.Features
                .Select((feature, index) => FlowAreaChoice(feature.FlowArea, index, isResult: false))
                .ToArray();
            MapResultMeshOriginSnapshot origin = new(
                "Geometry", geometryPath, revision,
                geometryOrigin.MeshDraftSHA256.ToUpperInvariant(),
                geometryOrigin.TopologyDraftSHA256.ToUpperInvariant(), null, 0, flowAreas,
                () => MapResultChartService.EnsureUnchanged(geometryPath, revision));
            // Keep the model alive only while all request-bound source identity checks are performed.
            // Chart execution reopens this exact owner in its own retained session context.
            return origin;
        }

        MapResultMeshResultOrigin resultOrigin = owner.ResultOrigin!;
        ResolvedMapResultNode selected = ResolveMapResultOwner(context,
            resultOrigin.ResultNode, resultOrigin.SelectionIndex);
        RequireExactSelectedResult(selected, resultOrigin.SelectedResultPath,
            resultOrigin.SelectedResultRevision);
        if (selected.Result.IsComputing())
            throw new InvalidOperationException("The origin Result is still computing. Wait for it to finish, refresh map Results, and retry.");
        Ras.Layers.Geometry resultGeometry = selected.Result.Geometry
            ?? throw new InvalidDataException("The selected Result has no embedded Geometry.");
        MapResultMeshFlowAreaChoice[] resultFlowAreas = resultGeometry.FlowAreaLayer.Features
            .Select((feature, index) => FlowAreaChoice(feature, index, isResult: true))
            .ToArray();
        return new MapResultMeshOriginSnapshot("Result",
            SourceFilePaths.Canonical(selected.ResultPath), selected.Revision.ToUpperInvariant(),
            string.Empty, string.Empty, resultOrigin.ResultNode, resultOrigin.SelectionIndex,
            resultFlowAreas, () => MapResultChartService.EnsureUnchanged(selected.ResultPath, selected.Revision));
    }

    private static MapResultMeshFlowAreaChoice FlowAreaChoice(FlowArea flowArea, int index, bool isResult)
    {
        return new MapResultMeshFlowAreaChoice(index, FlowAreaID(index), flowArea.Name ?? string.Empty,
            flowArea.GetMeshDataHash(isResult), Math.Max(0, flowArea.Mesh?.CellCount ?? 0),
            Math.Max(0, flowArea.Mesh?.FaceCount ?? 0), flowArea.Mesh is { CellCount: > 0, FaceCount: > 0 });
    }

    private static MapResultMeshFlowAreaChoice RequireFlowArea(MapResultMeshFlowAreaChoice[] choices,
        int flowAreaIndex, string flowAreaID, string expectedName)
    {
        MapResultMeshFlowAreaChoice[] matches = choices.Where(choice =>
            choice.FlowAreaIndex == flowAreaIndex &&
            string.Equals(choice.FlowAreaID, flowAreaID, StringComparison.Ordinal) &&
            string.Equals(choice.Name, expectedName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static int CountComputingChoices(MapResultSourcesResult sources) => sources.ActiveNodes
        .SelectMany(node => node.Selections)
        .SelectMany(selection => selection.Choices)
        .Where(choice => choice.IsComputing)
        .Select(choice => SourceFilePaths.Canonical(choice.ResultPath))
        .Distinct(StringComparer.Ordinal)
        .Count();

    private static string[] MatchingResultNames(ResolvedMapResultContext context,
        MapResultMeshFlowAreaChoice[] originFlowAreas, MapResultComputeReadLeaseScope? readLease)
    {
        var names = new List<string>();
        foreach (ResolvedMapResultNode result in context.ResultContexts
            .Where(result => result.SelectionIndices.Contains(0) &&
                result.Selections.Any(selection => selection.SelectionIndex == 0))
            .DistinctBy(result => (SourceFilePaths.Canonical(result.ResultPath), result.Revision),
                ResultSourceIdentityComparer.Instance))
        {
            if (result.Result.IsComputing() || readLease?.IsProtected(result.ResultPath) == true) continue;
            MapResultChartService.EnsureUnchanged(result.ResultPath, result.Revision);
            Ras.Layers.Geometry? geometry = result.Result.Geometry;
            bool matches = false;
            if (geometry is not null)
                matches = originFlowAreas.Any(origin => origin.HasMesh &&
                    origin.FlowAreaIndex < geometry.FlowAreaLayer.Features.Count &&
                    geometry.FlowAreaLayer.Features[origin.FlowAreaIndex].Mesh is { } mesh &&
                    mesh.CellCount == origin.CellCount && mesh.FaceCount == origin.FaceCount &&
                    geometry.FlowAreaLayer.Features[origin.FlowAreaIndex].PropertyTableSignature is { } signature &&
                    signature.MeshHash == origin.MeshHash);
            MapResultChartService.EnsureUnchanged(result.ResultPath, result.Revision);
            if (matches) names.Add(result.ActiveNode.Name);
        }
        return names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static bool MatchesSha(string expected, string actual) =>
        expected.Length == 64 && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static string MeshResultTimeSeriesHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string TimeSeriesUnits(UnitSystem unitSystem, string elementKind) => unitSystem switch
    {
        UnitSystem.USCustomary when elementKind == "Cell" => "ft",
        UnitSystem.SI when elementKind == "Cell" => "m",
        UnitSystem.USCustomary when elementKind == "Face" => "ft/s",
        UnitSystem.SI when elementKind == "Face" => "m/s",
        _ => string.Empty
    };

    private sealed record MapResultMeshOriginSnapshot(
        string Kind,
        string Path,
        string Revision,
        string MeshDraftSHA256,
        string TopologyDraftSHA256,
        CatalogTreeNodeLocator? ResultNode,
        int SelectionIndex,
        MapResultMeshFlowAreaChoice[] FlowAreas,
        Action EnsureUnchanged);

    private sealed class ResultSourceIdentityComparer : IEqualityComparer<(string Path, string Revision)>
    {
        internal static readonly ResultSourceIdentityComparer Instance = new();
        public bool Equals((string Path, string Revision) left, (string Path, string Revision) right) =>
            StringComparer.Ordinal.Equals(left.Path, right.Path) &&
            StringComparer.OrdinalIgnoreCase.Equals(left.Revision, right.Revision);
        public int GetHashCode((string Path, string Revision) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Path),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Revision));
    }
}
