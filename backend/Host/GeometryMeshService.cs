using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Topology;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Observables;
using Utility.Logging;
using Utility.Progress;
using Visual.Mapping.Observables;
using Visual.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private const int GeometryMeshPreviewCellLimit = 12_000;
    private const int GeometryMeshPreviewVertexLimit = 1_000_000;

    public static async Task<GeometryMeshSnapshot> LoadGeometryMeshAsync(GeometryMeshLoadRequest request)
    {
        VerifyGeometryMeshModel(request.Model);
        string revision = Revision(request.Model.AssetPath);
        if (request.Revision is not null && !string.Equals(request.Revision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        using var context = new ModelContext(ToModelRequest(request.Model));
        ApplyGeometryTopologyDraft(context, request.Model.AssetPath, revision, request.TopologyDraftState);
        string topologyFingerprint = GeometryMeshTopologyFingerprint(context);
        GeometryMeshDraftState state = string.IsNullOrWhiteSpace(request.DraftState)
            ? NewGeometryMeshDraft(request.Model.AssetPath, revision, topologyFingerprint)
            : ReadGeometryMeshDraft(request.DraftState, request.Model.AssetPath, revision);
        EnsureTopologyFingerprint(state, topologyFingerprint);
        GeometryMeshGenerationReport? generation = await ApplyGeometryMeshMutationsAsync(context,
            request.Model.AssetPath, revision, state, replayPropertyTableComputations: false);
        GeometryMeshSnapshot snapshot = GeometryMeshSnapshotFor(context, revision, state, request.SelectedRegionIDs ?? [], generation);
        if (Revision(request.Model.AssetPath) != revision) throw new SourceFileChangedException();
        return await Task.FromResult(snapshot);
    }

    public static async Task<GeometryMeshSnapshot> EditGeometryMeshAsync(GeometryMeshEditRequest request)
    {
        VerifyGeometryMeshModel(request.Model);
        GeometryMeshDraftState state = ReadGeometryMeshDraft(request.DraftState, request.Model.AssetPath, request.Revision);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();

        using var context = new ModelContext(ToModelRequest(request.Model));
        ApplyGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, request.TopologyDraftState);
        string topologyFingerprint = GeometryMeshTopologyFingerprint(context);
        EnsureTopologyFingerprint(state, topologyFingerprint);
        GeometryMeshGenerationReport? generation = await ApplyGeometryMeshMutationsAsync(context,
            request.Model.AssetPath, request.Revision, state, replayPropertyTableComputations: false);
        var mutation = request.Mutation ?? throw new ArgumentException("Missing geometry mesh edit.");
        bool changed;
        GeometryMeshMutation retainedMutation = mutation;
        GeometryMeshGenerationReport? currentGeneration = null;
        GeometryMeshDiagnosticHistoryEvent? diagnosticHistoryEvent = null;
        if (mutation.Operation == "generate")
        {
            currentGeneration = GenerateGeometryMesh(context, mutation.OperationID, out diagnosticHistoryEvent);
            changed = currentGeneration.MeshWasReplaced;
            if (changed)
                retainedMutation = mutation with
                {
                    ExpectedMeshFingerprint = GeometryMeshFlowAreaFingerprint(context),
                    GeneratedMeshPayload = GeometryMeshArtifactForCurrentFlowAreas(context),
                    GeneratedReport = currentGeneration
                };
            generation = currentGeneration;
        }
        else
        {
            changed = ApplyGeometryMeshMutation(context, mutation);
            currentGeneration = generation;
        }

        GeometryMeshDraftState edited = changed
            ? state with { Mutations = [.. state.Mutations, retainedMutation] }
            : state;
        GeometryMeshSnapshot snapshot = GeometryMeshSnapshotFor(context, request.Revision, edited,
            request.SelectedRegionIDs ?? [], currentGeneration, diagnosticHistoryEvent);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        return await Task.FromResult(snapshot);
    }

    public static async Task<GeometryMeshDegreeScan> ScanGeometryMeshDegreeErrorsAsync(GeometryMeshDegreeScanRequest request)
    {
        VerifyGeometryMeshModel(request.Model);
        if (Revision(request.Model.AssetPath) != request.Revision) throw new SourceFileChangedException();
        GeometryMeshDraftState state = ReadGeometryMeshDraft(request.DraftState, request.Model.AssetPath, request.Revision);
        using var context = new ModelContext(ToModelRequest(request.Model));
        ApplyGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, request.TopologyDraftState);
        string topologyFingerprint = GeometryMeshTopologyFingerprint(context);
        EnsureTopologyFingerprint(state, topologyFingerprint);
        _ = await ApplyGeometryMeshMutationsAsync(context, request.Model.AssetPath, request.Revision,
            state, replayPropertyTableComputations: false);
        var (geometry, _) = GeometryMeshTopologyFor(context);
        int threshold = Math.Clamp(request.MaxFacesPerNode, 3, 64);
        var issues = new List<GeometryMeshDegreeIssue>();
        for (int meshIndex = 0; meshIndex < geometry.FlowAreaLayer.Features.Count; meshIndex++)
        {
            Observable2DFlowArea flowArea = geometry.FlowAreaLayer.Features[meshIndex];
            Mesh? mesh = flowArea.Mesh;
            if (mesh is null) continue;
            var sourceErrors = new List<MeshNodeDegreeError>();
            MeshNodeDegreeScan.FindHighDegreeNodes(mesh, meshIndex, threshold, sourceErrors);
            foreach (MeshNodeDegreeError issue in sourceErrors)
            {
                bool finiteNode = double.IsFinite(issue.NodePoint.X) && double.IsFinite(issue.NodePoint.Y);
                GeometryMeshExtent? cellsExtent = MeshExtent(issue.CellsExtent(mesh));
                GeometryMeshDegreeCell[] cells = request.IncludeIncidentCells
                    ? issue.Cells
                        .Where(cellIndex => cellIndex >= 0 && cellIndex < mesh.CellCount)
                        .Select(cellIndex =>
                        {
                            Point[] points = mesh.CellPolygon(cellIndex).GetPointBufferUnsafe().Span.ToArray();
                            if (points.Length < 3 || points.Any(point =>
                                    !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return null;
                            return new GeometryMeshDegreeCell(cellIndex,
                                points.Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray());
                        })
                        .Where(cell => cell is not null)
                        .Select(cell => cell!)
                        .ToArray()
                    : [];
                string flowAreaID = FlowAreaID(meshIndex);
                issues.Add(new GeometryMeshDegreeIssue(
                    flowAreaID + ":node:" + issue.NodeIndex.ToString(CultureInfo.InvariantCulture),
                    flowAreaID, flowArea.Name, meshIndex, issue.NodeIndex, issue.FaceCount,
                    finiteNode ? issue.NodePoint.X : null, finiteNode ? issue.NodePoint.Y : null, cellsExtent, cells));
            }
        }
        GeometryMeshDegreeIssue[] sorted = issues.OrderByDescending(issue => issue.FaceCount)
            .ThenBy(issue => issue.MeshIndex).ThenBy(issue => issue.NodeIndex).ToArray();
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        return await Task.FromResult(new GeometryMeshDegreeScan(threshold, sorted));
    }

    /// <summary>
    /// Runs the original ObservableFlowAreaLayer property-table action against
    /// an isolated in-memory model rebuilt from the exact geometry, topology
    /// draft, and ordered mesh child ledger. A successful result adds a replay
    /// intent to that ledger; the parent later reruns the same source algorithm
    /// on its staged HDF before the single atomic save.
    /// </summary>
    internal static async Task<GeometryMeshPropertyTablesResult> ComputeGeometryMeshPropertyTablesDuplexAsync(
        GeometryMeshPropertyTablesRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        GeometryMeshModelRequest model = request.Model
            ?? throw new ArgumentException("Missing geometry property-table request model.");
        VerifyGeometryMeshModel(model);
        string revision = Revision(model.AssetPath);
        if (!string.Equals(revision, request.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        GeometryMeshDraftState state = ReadGeometryMeshDraft(request.DraftState, model.AssetPath, revision);

        using var context = new ModelContext(ToModelRequest(model));
        ApplyGeometryTopologyDraft(context, model.AssetPath, revision, request.TopologyDraftState);
        EnsureTopologyFingerprint(state, GeometryMeshTopologyFingerprint(context));
        GeometryMeshGenerationReport? generation = await ApplyGeometryMeshMutationsAsync(context,
            model.AssetPath, revision, state, replayPropertyTableComputations: false);
        if (Revision(model.AssetPath) != revision) throw new SourceFileChangedException();

        var (geometry, _) = GeometryMeshTopologyFor(context);
        if (!geometry.CanMutate)
            throw new NotSupportedException("This geometry is a result or is currently being updated and cannot be edited.");

        var reporter = new ProgressReporter();
        var job = new BatchJob(reporter, "Processing property tables for " + geometry.Name);
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new BackendOperationProgress(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new BackendOperationProgress(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));

        int cancelRequested = 0;
        using var stopControls = new CancellationTokenSource();
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls
                                   .ReadAllAsync(stopControls.Token).ConfigureAwait(false))
                {
                    if (frame.Control.TryGetProperty("type", out JsonElement typeValue) &&
                        typeValue.GetString() == "cancel")
                    {
                        if (Interlocked.Exchange(ref cancelRequested, 1) == 0)
                            job.RequestCancel();
                    }
                }
            }
            catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
        }, stopControls.Token);

        report?.Invoke(new BackendOperationProgress(TaskName: job.Name));
        OperationResult operation;
        try
        {
            operation = await geometry.FlowAreaLayer.ComputePropertyTablesAsync(job).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            operation = OperationResult.Fail(GeneralMessageCodes.GetExceptionOccurred(
                "Computing Property Tables", exception));
        }
        finally
        {
            stopControls.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        job.MarkComplete(operation.Result);

        if (!string.Equals(Revision(model.AssetPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        bool cancelled = !operation.Result && operation.Messages.Any(message =>
            string.Equals(message.MessageType.Code, "Generic_TaskCancelled", StringComparison.Ordinal));
        GeometryMeshDraftState resultState = operation.Result
            ? state with { Mutations = [.. state.Mutations, new GeometryMeshMutation("computePropertyTables")] }
            : state;
        GeometryMeshSnapshot snapshot = GeometryMeshSnapshotFor(context, revision, resultState,
            request.SelectedRegionIDs ?? [], generation);
        return new GeometryMeshPropertyTablesResult(operation.Result, cancelled,
            operation.Messages.Select(message => message.EvaluatedMessage).ToArray(), snapshot);
    }

    /// <summary>
    /// Runs the retained ConceptualMeshVisualContext Generate Mesh action on
    /// the exact mesh/topology child draft. Source progress and cancellation
    /// travel through the original ProgressReporter; the generated artifact is
    /// still committed only by the parent geometry save.
    /// </summary>
    internal static async Task<GeometryMeshGenerateResult> GenerateGeometryMeshDuplexAsync(
        GeometryMeshGenerateRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        GeometryMeshModelRequest model = request.Model
            ?? throw new ArgumentException("Missing geometry mesh-generation request model.");
        if (string.IsNullOrWhiteSpace(request.OperationID))
            throw new ArgumentException("Missing geometry mesh-generation operation identity.");
        VerifyGeometryMeshModel(model);
        string revision = Revision(model.AssetPath);
        if (!string.Equals(revision, request.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        GeometryMeshDraftState state = ReadGeometryMeshDraft(request.DraftState, model.AssetPath, revision);

        using var context = new ModelContext(ToModelRequest(model));
        ApplyGeometryTopologyDraft(context, model.AssetPath, revision, request.TopologyDraftState);
        EnsureTopologyFingerprint(state, GeometryMeshTopologyFingerprint(context));
        GeometryMeshGenerationReport? priorGeneration = await ApplyGeometryMeshMutationsAsync(context,
            model.AssetPath, revision, state, replayPropertyTableComputations: false);
        if (!string.Equals(Revision(model.AssetPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        var (geometry, topologyView) = GeometryMeshTopologyFor(context);
        if (!geometry.CanMutate)
            throw new NotSupportedException("This geometry is a result or is currently being updated and cannot be edited.");
        if (topologyView is null)
        {
            GeometryMeshGenerationReport unavailable = new(false, false, false, null,
                ["The source conceptual mesh topology is unavailable."]);
            GeometryMeshSnapshot unavailableSnapshot = GeometryMeshSnapshotFor(context, revision, state,
                request.SelectedRegionIDs ?? [], unavailable);
            return new GeometryMeshGenerateResult(request.OperationID, revision, false, unavailableSnapshot);
        }
        Topology completedTopology = topologyView.Topology.Clone();
        OperationResult completion = completedTopology.Complete();
        if (!completion.Result)
        {
            GeometryMeshGenerationReport incomplete = GenerationReport(completion,
                meshWasReplaced: false, mesh: null);
            GeometryMeshSnapshot incompleteSnapshot = GeometryMeshSnapshotFor(context, revision, state,
                request.SelectedRegionIDs ?? [], incomplete);
            return new GeometryMeshGenerateResult(request.OperationID, revision, false, incompleteSnapshot);
        }

        var reporter = new ProgressReporter();
        var job = new BatchJob(reporter, geometry.Name + " Mesh Generation");
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new BackendOperationProgress(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new BackendOperationProgress(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));

        int cancelRequested = 0;
        using var stopControls = new CancellationTokenSource();
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls
                                   .ReadAllAsync(stopControls.Token).ConfigureAwait(false))
                {
                    if (frame.Control.TryGetProperty("type", out JsonElement typeValue) &&
                        typeValue.GetString() == "cancel")
                    {
                        if (Interlocked.Exchange(ref cancelRequested, 1) == 0)
                            job.RequestCancel();
                    }
                }
            }
            catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
        }, stopControls.Token);

        report?.Invoke(new BackendOperationProgress(TaskName: job.Name));
        GeometryMeshGenerationReport? generated = null;
        GeometryMeshDiagnosticHistoryEvent? history = null;
        try
        {
            (generated, history) = await Task.Run(() =>
            {
                GeometryMeshGenerationReport result = GenerateGeometryMesh(context, request.OperationID,
                    out GeometryMeshDiagnosticHistoryEvent? diagnostic, reporter, completedTopology, completion);
                return (result, diagnostic);
            }).ConfigureAwait(false);
        }
        finally
        {
            stopControls.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            reporter.ReportProgress(100);
            job.MarkComplete(generated?.Succeeded == true);
        }

        if (!string.Equals(Revision(model.AssetPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        bool cancelled = job.Reporter.CancelRequested;
        if (cancelled) history = null; // The retained action exits before opening mesh diagnostics on cancel.
        GeometryMeshDraftState resultState = state;
        if (generated is { MeshWasReplaced: true })
        {
            GeometryMeshMutation retained = new GeometryMeshMutation("generate", OperationID: request.OperationID) with
            {
                ExpectedMeshFingerprint = GeometryMeshFlowAreaFingerprint(context),
                GeneratedMeshPayload = GeometryMeshArtifactForCurrentFlowAreas(context),
                GeneratedReport = generated
            };
            resultState = state with { Mutations = [.. state.Mutations, retained] };
        }

        GeometryMeshSnapshot snapshot = GeometryMeshSnapshotFor(context, revision, resultState,
            request.SelectedRegionIDs ?? [], generated ?? priorGeneration, history);
        return new GeometryMeshGenerateResult(request.OperationID, revision, cancelled, snapshot);
    }

    /// <summary>
    /// Replays the mesh child ledger into the already-open staged model. The
    /// parent invokes this after its topology ledger, so generated meshes and
    /// patch edits are validated against the exact topology shown in preview.
    /// The parent owns the single atomic ModelEditorAdapter.SaveAsync write.
    /// </summary>
    private static async Task ApplyGeometryMeshDraft(ModelContext context, string assetPath, string revision,
        string? encodedState)
    {
        if (string.IsNullOrWhiteSpace(encodedState)) return;
        GeometryMeshDraftState state = ReadGeometryMeshDraft(encodedState, assetPath, revision);
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        EnsureTopologyFingerprint(state, GeometryMeshTopologyFingerprint(context));
        _ = await ApplyGeometryMeshMutationsAsync(context, assetPath, revision, state,
            replayPropertyTableComputations: true);
    }

    /// <summary>
    /// Runs the retained source Geometry.PrepareForCompute workflow only when
    /// requested by the mesh draft. ModelEditorAdapter calls this on its
    /// staged ModelContext after topology, mesh, culvert, image and ordinary
    /// field edits have been applied. Geometry.PrepareForCompute calls Save;
    /// in this path that write is confined to the staging HDF. A failed source
    /// OperationResult aborts the parent save, so the user's original HDF is
    /// never replaced by a partially prepared file.
    /// </summary>
    private static void ApplyGeometryMeshPrepare(ModelContext context, string assetPath,
        string revision, string? encodedState)
    {
        if (string.IsNullOrWhiteSpace(encodedState)) return;
        GeometryMeshDraftState state = ReadGeometryMeshDraft(encodedState, assetPath, revision);
        if (!state.Mutations.Any(mutation => mutation.Operation == "prepare")) return;
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        EnsureTopologyFingerprint(state, GeometryMeshTopologyFingerprint(context));
        var (geometry, _) = GeometryMeshTopologyFor(context);
        if (!geometry.CanMutate)
            throw new NotSupportedException("This geometry is a result or is currently being updated and cannot be prepared.");

        OperationResult result = geometry.Geometry.PrepareForCompute(ProgressReporter.None());
        if (result.Result) return;

        GeometrySummary? summary = geometry.Geometry.PrepareSummary;
        string? failureReason = summary?.FailureReason;
        if (string.IsNullOrWhiteSpace(failureReason))
            failureReason = result.GetConcatenatedMessages("; ");
        string? prepareLog = summary?.PrepareLog;
        var message = new StringBuilder("Geometry preparation failed.");
        if (!string.IsNullOrWhiteSpace(failureReason)) message.Append("\n\n").Append(failureReason);
        if (!string.IsNullOrWhiteSpace(prepareLog)) message.Append("\n\n").Append("Prepare log:").Append('\n').Append(prepareLog);
        throw new GeometryMeshPrepareException(message.ToString(), summary);
    }

    private static ModelRequest ToModelRequest(GeometryMeshModelRequest model) =>
        new(model.AssetPath, model.Kind, model.ProjectPath, model.ProjectXml,
            CoreLayerContext: model.CoreLayerContext);

    private static void VerifyGeometryMeshModel(GeometryMeshModelRequest model)
    {
        if (model.Kind != "Geometry") throw new ArgumentException("Mesh editing requires a geometry model.");
        if (string.IsNullOrWhiteSpace(model.AssetPath) || !File.Exists(model.AssetPath))
            throw new FileNotFoundException("The geometry source file was not found.", model.AssetPath);
    }

    private static GeometryMeshDraftState NewGeometryMeshDraft(string assetPath, string revision, string topologyFingerprint) =>
        new(SourceFilePaths.Canonical(assetPath), revision, topologyFingerprint, []);

    private static GeometryMeshDraftState ReadGeometryMeshDraft(string encoded, string assetPath, string revision)
    {
        if (string.IsNullOrWhiteSpace(encoded)) throw new ArgumentException("Missing geometry mesh draft state.");
        GeometryMeshDraftState state;
        try
        {
            state = JsonSerializer.Deserialize<GeometryMeshDraftState>(encoded, BackendJson.Options)
                ?? throw new ArgumentException("Invalid geometry mesh draft state.");
        }
        catch (JsonException exception) { throw new ArgumentException("Invalid geometry mesh draft state.", exception); }
        if (!string.Equals(state.AssetPath, SourceFilePaths.Canonical(assetPath), StringComparison.Ordinal)
            || !string.Equals(state.Revision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (string.IsNullOrWhiteSpace(state.TopologyFingerprint) || state.Mutations is null)
            throw new ArgumentException("The geometry mesh draft state is incomplete.");
        return state;
    }

    private static void EnsureTopologyFingerprint(GeometryMeshDraftState state, string currentFingerprint)
    {
        if (!string.Equals(state.TopologyFingerprint, currentFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The source topology changed after this mesh draft was created. Discard the mesh draft and reload it against the current topology.");
    }

    private static (ObservableGeometry Geometry, ObservableConceptualMeshTopology? Topology) GeometryMeshTopologyFor(ModelContext context)
    {
        if (context.Layer is not Geometry || context.Observable is not ObservableGeometry geometry)
            throw new ArgumentException("The source model is not a geometry.");
        if (geometry.ConceptualMeshLayer.Layer.LoadFailed)
            return (geometry, null);
        return (geometry, geometry.ConceptualMeshLayer.Topology as ObservableConceptualMeshTopology);
    }

    private static string GeometryMeshTopologyFingerprint(ModelContext context)
    {
        var (_, topology) = GeometryMeshTopologyFor(context);
        if (topology is null) return "unavailable";
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            topology.Topology.StreamOut(writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static async Task<GeometryMeshGenerationReport?> ApplyGeometryMeshMutationsAsync(ModelContext context,
        string assetPath, string revision, GeometryMeshDraftState state, bool replayPropertyTableComputations,
        Action<BackendOperationProgress>? report = null,
        BackendDuplexControlReader? duplexControls = null)
    {
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        GeometryMeshGenerationReport? generation = null;
        bool hasPropertyTableReplay = replayPropertyTableComputations &&
            state.Mutations.Any(mutation => mutation.Operation == "computePropertyTables");
        ProgressReporter? replayReporter = null;
        CancellationTokenSource? stopControls = null;
        Task? controlPump = null;
        if (hasPropertyTableReplay)
        {
            ProgressReporter currentReplayReporter = new();
            replayReporter = currentReplayReporter;
            currentReplayReporter.ProgressReported += (sender, percent) => report?.Invoke(new BackendOperationProgress(
                Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
            currentReplayReporter.MessageReported += (sender, message) => report?.Invoke(new BackendOperationProgress(
                Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));
            report?.Invoke(new BackendOperationProgress(TaskName: "Replaying Geometry property-table computation"));
            if (duplexControls is not null)
            {
                CancellationTokenSource currentStopControls = new();
                stopControls = currentStopControls;
                controlPump = Task.Run(async () =>
                {
                    await foreach (BackendDuplexControlFrame frame in duplexControls
                                       .ReadAllAsync(currentStopControls.Token).ConfigureAwait(false))
                    {
                        if (frame.Control.TryGetProperty("type", out JsonElement typeValue) &&
                            string.Equals(typeValue.GetString(), "cancel", StringComparison.Ordinal))
                            currentReplayReporter.RequestCancel();
                    }
                }, currentStopControls.Token);
            }
        }

        try
        {
            foreach (GeometryMeshMutation mutation in state.Mutations)
            {
                // Prepare is a request marker, not a replayed mutation. The parent
                // invokes the actual source workflow after all geometry drafts.
                if (mutation.Operation == "prepare") continue;
                if (mutation.Operation == "computePropertyTables")
                {
                    // Ordinary previews retain the user's successful source computation
                    // intent. Chart reads replay it in an isolated ModelContext so an
                    // unsaved child draft can be plotted without committing its source HDF.
                    if (!replayPropertyTableComputations) continue;
                    var (geometry, _) = GeometryMeshTopologyFor(context);
                    if (!geometry.CanMutate)
                        throw new NotSupportedException("This geometry is a result or is currently being updated and cannot be edited.");
                    ProgressReporter reporter = replayReporter ?? new ProgressReporter();
                    var replayJob = new BatchJob(reporter,
                        "Processing property tables for " + geometry.Name);
                    OperationResult result = await geometry.FlowAreaLayer.ComputePropertyTablesAsync(replayJob)
                        .ConfigureAwait(false);
                    replayJob.MarkComplete(result.Result);
                    if (!result.Result)
                    {
                        bool cancelled = reporter.CancelRequested || result.Messages.Any(message =>
                            string.Equals(message.MessageType.Code, "Generic_TaskCancelled", StringComparison.Ordinal));
                        if (cancelled) throw new PropertyTableReplayCancelledException();
                        throw new InvalidOperationException("Property table computation failed: "
                            + result.GetConcatenatedMessages("; "));
                    }
                    continue;
                }
                if (mutation.Operation == "generate")
                {
                    if (mutation.GeneratedMeshPayload is null || mutation.GeneratedReport is null
                        || !mutation.GeneratedReport.MeshWasReplaced)
                        throw new InvalidOperationException("The mesh draft does not contain the exact generated preview.");
                    ApplyGeometryMeshArtifact(context, mutation.GeneratedMeshPayload, mutation.GeneratedReport);
                    generation = mutation.GeneratedReport;
                    string actual = GeometryMeshFlowAreaFingerprint(context);
                    if (!string.Equals(actual, mutation.ExpectedMeshFingerprint, StringComparison.Ordinal))
                        throw new InvalidOperationException("The saved mesh preview payload failed its source fingerprint check.");
                }
                else _ = ApplyGeometryMeshMutation(context, mutation);
            }
        }
        finally
        {
            if (stopControls is not null)
            {
                stopControls.Cancel();
                try { if (controlPump is not null) await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
                finally { stopControls.Dispose(); }
            }
        }
        return generation;
    }

    private sealed class PropertyTableReplayCancelledException()
        : OperationCanceledException("The Geometry property-table replay was cancelled.");

    private static bool ApplyGeometryMeshMutation(ModelContext context, GeometryMeshMutation mutation)
    {
        var (geometry, topology) = GeometryMeshTopologyFor(context);
        if (!geometry.CanMutate) throw new NotSupportedException("This geometry is a result or is currently being updated and cannot be edited.");
        switch (mutation.Operation)
        {
            case "prepare":
                return true;
            case "setFlowAreaField":
            {
                string[] ids = mutation.FlowAreaIDs ?? throw new ArgumentException("A flow-area source row is required.");
                string[] expectedNames = mutation.ExpectedFlowAreaNames ?? throw new ArgumentException("The flow-area identity guard is missing.");
                if (ids.Length != 1 || expectedNames.Length != 1 || mutation.Value is null || mutation.FieldID is null)
                    throw new ArgumentException("A flow-area edit requires one row, a field and a value.");
                Observable2DFlowArea flowArea = FindFlowArea(geometry, ids[0], expectedNames[0]);
                switch (mutation.FieldID)
                {
                    case "Name":
                        if (string.Equals(flowArea.Name, mutation.Value, StringComparison.Ordinal)) return false;
                        flowArea.Name = mutation.Value;
                        return true;
                    case "TargetedCellSpacing":
                    {
                        double value = double.Parse(mutation.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (double.IsInfinity(value)) throw new ArgumentException("Target cell spacing must be finite or NaN for source-derived spacing.");
                        if (flowArea.TargetedCellSpacing.Equals(value)) return false;
                        flowArea.TargetedCellSpacing = value;
                        return true;
                    }
                    default: throw new ArgumentException("Unsupported source flow-area field: " + mutation.FieldID);
                }
            }
            case "setPatchField":
            {
                ObservableConceptualMeshTopology sourceTopology = RequireGeometryMeshTopology(topology);
                int[] ids = RequireRegionIDs(mutation.FeatureIDs);
                int before = sourceTopology.Topology.GetDataHash();
                foreach (int fid in ids)
                {
                    ObservableConceptualMeshControlPoint point = FindRegion(sourceTopology, fid);
                    SetRegionField(point, mutation.FieldID ?? throw new ArgumentException("A region field is required."),
                        mutation.Value ?? throw new ArgumentException("A region value is required."));
                }
                return before != sourceTopology.Topology.GetDataHash();
            }
            case "resizePatch":
            {
                ObservableConceptualMeshTopology sourceTopology = RequireGeometryMeshTopology(topology);
                int[] ids = RequireRegionIDs(mutation.FeatureIDs);
                PatchType kind = EnsureOnePatchType(sourceTopology, ids);
                float ratio = mutation.Value switch
                {
                    "0.5" => 0.5f,
                    "2" => 2f,
                    _ => throw new ArgumentException("The source patch quick-size ratio must be one half or double.")
                };
                int before = sourceTopology.Topology.GetDataHash();
                if (kind is PatchType.TrianglePaver or PatchType.Cartesian)
                {
                    // TrianglePatchQuickEditor and CartesianPatchQuickEditor bind
                    // their ratio command to PatchEditor.ControlPoint (the first
                    // row in the source selection collection).
                    if (mutation.Axis is not null) throw new ArgumentException("This patch quick-size operation has no axis.");
                    _ = ApplyGeometryTopologyMutation(sourceTopology,
                        new GeometryTopologyMutation(mutation.Value == "0.5" ? "halfSize" : "doubleSize", "region", [ids[0]]));
                }
                else if (kind == PatchType.Quad)
                {
                    if (mutation.Axis is not ("NorthSouth" or "EastWest"))
                        throw new ArgumentException("Quad quick sizing requires a NorthSouth or EastWest axis.");
                    int[] arcIDs = RequireQuadPatchSideArcIDs(sourceTopology, ids, mutation.Axis);
                    foreach (int arcID in arcIDs)
                    {
                        ObservableConceptualMeshArc arc = FindArc(sourceTopology, arcID);
                        if (arc.SizeFunction == LinearSizeFunction.Constant)
                            SetValidatedProperty(arc, "ConstantSize", arc.ConstantSize * ratio);
                        else if (arc.SizeFunction == LinearSizeFunction.CountWithBias)
                            SetValidatedProperty(arc, "PointCount", (int)((float)arc.PointCount * ratio));
                        else throw new NotSupportedException("The source quad quick editor cannot resize an arc with an Unset size function.");
                    }
                }
                else throw new NotSupportedException("The source editor does not define quick sizing for this patch type.");
                return before != sourceTopology.Topology.GetDataHash();
            }
            case "setPatchDimensionMode":
            {
                ObservableConceptualMeshTopology sourceTopology = RequireGeometryMeshTopology(topology);
                int[] ids = RequireRegionIDs(mutation.FeatureIDs);
                if (EnsureOnePatchType(sourceTopology, ids) != PatchType.Quad)
                    throw new ArgumentException("Patch side settings are available only for Quad patches.");
                string axis = RequireQuadAxis(mutation.Axis);
                LinearSizeFunction mode = mutation.Value switch
                {
                    "Constant" => LinearSizeFunction.Constant,
                    "CountWithBias" => LinearSizeFunction.CountWithBias,
                    _ => throw new ArgumentException("The source quad dimension editor supports Constant and CountWithBias modes.")
                };
                int[] arcIDs = RequireQuadPatchSideArcIDs(sourceTopology, ids, axis);
                if (arcIDs.Length == 0) return false;
                int before = sourceTopology.Topology.GetDataHash();
                foreach (int arcID in arcIDs) SetValidatedProperty(FindArc(sourceTopology, arcID), "SizeFunction", mode);
                return before != sourceTopology.Topology.GetDataHash();
            }
            case "applyPatchDimension":
            {
                ObservableConceptualMeshTopology sourceTopology = RequireGeometryMeshTopology(topology);
                int[] ids = RequireRegionIDs(mutation.FeatureIDs);
                if (EnsureOnePatchType(sourceTopology, ids) != PatchType.Quad)
                    throw new ArgumentException("Patch side settings are available only for Quad patches.");
                string axis = RequireQuadAxis(mutation.Axis);
                string valueText = mutation.Value ?? throw new ArgumentException("A patch side value is required.");
                int[] arcIDs = RequireQuadPatchSideArcIDs(sourceTopology, ids, axis);
                if (arcIDs.Length == 0) return false;
                int before = sourceTopology.Topology.GetDataHash();
                LinearSizeFunction? mode = UniformSizeFunction(sourceTopology, arcIDs);
                if (mode is null) throw new InvalidOperationException("Choose one source size function before applying a mixed patch side value.");
                if (mode == LinearSizeFunction.Constant)
                {
                    float parsed = ParseFloat(valueText);
                    foreach (int arcID in arcIDs) SetValidatedProperty(FindArc(sourceTopology, arcID), "ConstantSize", parsed);
                }
                else if (mode == LinearSizeFunction.CountWithBias)
                {
                    int parsed = int.Parse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    foreach (int arcID in arcIDs) SetValidatedProperty(FindArc(sourceTopology, arcID), "PointCount", parsed);
                }
                else throw new NotSupportedException("The source quad dimension editor cannot apply a value while its size function is Unset.");
                return before != sourceTopology.Topology.GetDataHash();
            }
            default: throw new ArgumentException("Unsupported geometry mesh operation: " + mutation.Operation);
        }
    }

    private static GeometryMeshSnapshot GeometryMeshSnapshotFor(ModelContext context, string revision,
        GeometryMeshDraftState state, int[] selectedRegionIDs, GeometryMeshGenerationReport? generation,
        GeometryMeshDiagnosticHistoryEvent? diagnosticHistoryEvent = null)
    {
        var (geometry, topology) = GeometryMeshTopologyFor(context);
        bool propertyTablesQueuedForParentSave = state.Mutations.Any(mutation =>
            mutation.Operation == "computePropertyTables");
        GeometryMeshFlowAreaRow[] flowAreas = geometry.FlowAreaLayer.Features
            .Select((flowArea, index) => FlowAreaSummary(flowArea, index,
                propertyTablesQueuedForParentSave)).ToArray();
        GeometryTopologyFeatureTable regions = topology is null
            ? new GeometryTopologyFeatureTable("region", [], [], [])
            : RegionTable(topology);
        int[] selected = selectedRegionIDs.Distinct().OrderBy(value => value).ToArray();
        string topologyFingerprint = GeometryMeshTopologyFingerprint(context);
        GeometryMeshPatchSizing sizing = PatchSizingFor(topology, selected, revision, topologyFingerprint);
        GeometryMeshMapData map = GeometryMeshMapFor(geometry, topology);
        return new GeometryMeshSnapshot(
            Revision: revision,
            DraftState: JsonSerializer.Serialize(state, BackendJson.Options),
            TopologyFingerprint: topologyFingerprint,
            CanEdit: geometry.CanMutate,
            IsDirty: state.Mutations.Length > 0,
            FlowAreas: flowAreas,
            Regions: regions,
            SelectedRegionIDs: selected,
            PatchSizing: sizing,
            GenerationReport: generation,
            PrepareSummary: PrepareSummaryFor(geometry.Geometry.PrepareSummary),
            Map: map,
            PreparePending: state.Mutations.Any(mutation => mutation.Operation == "prepare"),
            DiagnosticHistoryEvent: diagnosticHistoryEvent);
    }

    private static GeometryMeshMapData GeometryMeshMapFor(ObservableGeometry geometry,
        ObservableConceptualMeshTopology? topology)
    {
        GeometryTopologyMap sourceTopology = topology is null
            ? new GeometryTopologyMap(null, [])
            : MapData(topology).Map;
        int totalCells = geometry.FlowAreaLayer.Features.Sum(flowArea => Math.Max(0, flowArea.Mesh?.CellCount ?? 0));
        int stride = totalCells <= GeometryMeshPreviewCellLimit
            ? 1
            : (int)Math.Ceiling(totalCells / (double)GeometryMeshPreviewCellLimit);
        var cells = new List<GeometryMeshMapCell>(Math.Min(totalCells, GeometryMeshPreviewCellLimit));
        int invalidCandidates = 0;
        int renderedVertices = 0;
        int globalCellIndex = 0;
        var extents = new List<GeometryTopologyExtent>();
        if (sourceTopology.Extent is { } topologyExtent && ValidExtent(topologyExtent)) extents.Add(topologyExtent);

        for (int meshIndex = 0; meshIndex < geometry.FlowAreaLayer.Features.Count; meshIndex++)
        {
            Observable2DFlowArea flowArea = geometry.FlowAreaLayer.Features[meshIndex];
            Mesh? mesh = flowArea.Mesh;
            if (mesh is null) continue;
            GeometryMeshExtent? meshExtent = MeshExtent(mesh.Extent);
            if (meshExtent is { } extent)
                extents.Add(new GeometryTopologyExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY));

            string flowAreaID = FlowAreaID(meshIndex);
            for (int cellIndex = 0; cellIndex < mesh.CellCount; cellIndex++, globalCellIndex++)
            {
                if (globalCellIndex % stride != 0) continue;
                Polygon polygon;
                try { polygon = mesh.CellPolygon(cellIndex); }
                catch (Exception) { invalidCandidates++; continue; }

                int pointCount = polygon.Points.Count;
                if (pointCount < 3 || pointCount > GeometryMeshPreviewVertexLimit
                    || renderedVertices + pointCount > GeometryMeshPreviewVertexLimit)
                {
                    invalidCandidates++;
                    continue;
                }
                Point[] points = polygon.Points.ToArray();
                if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                {
                    invalidCandidates++;
                    continue;
                }

                cells.Add(new GeometryMeshMapCell(
                    flowAreaID + ":cell:" + cellIndex.ToString(CultureInfo.InvariantCulture),
                    flowAreaID, flowArea.Name, meshIndex, cellIndex,
                    points.Select(point => new GeometryTopologyCoordinate(point.X, point.Y)).ToArray()));
                renderedVertices += points.Length;
            }
        }

        GeometryTopologyExtent? extentUnion = extents.Count == 0 ? null : new GeometryTopologyExtent(
            extents.Min(extent => extent.MinX), extents.Min(extent => extent.MinY),
            extents.Max(extent => extent.MaxX), extents.Max(extent => extent.MaxY));
        return new GeometryMeshMapData(extentUnion, sourceTopology, cells.ToArray(), totalCells,
            cells.Count, Math.Max(0, totalCells - cells.Count), invalidCandidates,
            GeometryMeshPreviewCellLimit, GeometryMeshPreviewVertexLimit);
    }

    private static bool ValidExtent(GeometryTopologyExtent extent) =>
        double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY)
        && double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY)
        && extent.MinX <= extent.MaxX && extent.MinY <= extent.MaxY;

    private static GeometryMeshFlowAreaRow FlowAreaSummary(Observable2DFlowArea feature, int rowIndex,
        bool propertyTablesQueuedForParentSave)
    {
        FlowArea flowArea = feature.FlowArea;
        Mesh? mesh = feature.Mesh;
        double spacing = feature.TargetedCellSpacing;
        GeometryMeshMetadataItem[] metadata =
        [
            new("Cell Count", "Cell Count", flowArea.CellCount.ToString("N0", CultureInfo.InvariantCulture)),
            new("Property Tables Pinned", "Property Tables Pinned", flowArea.PropertyTablesPinned.ToString()),
            new("Mesh Errors", "Mesh Errors", flowArea.HasMeshErrors.ToString())
        ];
        return new GeometryMeshFlowAreaRow(FlowAreaID(rowIndex), rowIndex, feature.Name,
            double.IsNaN(spacing) ? null : spacing, mesh is not null,
            mesh?.CellCount ?? 0, mesh?.FaceCount ?? 0, mesh?.FacePointCount ?? 0,
            mesh is null ? null : MeshExtent(mesh.Extent), flowArea.HasMeshErrors,
            flowArea.PropertyTables is not null, propertyTablesQueuedForParentSave,
            flowArea.PropertyTablesPinned, metadata);
    }

    private static GeometryMeshPrepareSummary? PrepareSummaryFor(GeometrySummary? summary)
    {
        if (summary is null) return null;
        return new GeometryMeshPrepareSummary(summary.Success, summary.FailureReason, summary.SoftwareVersion,
            summary.StartTime.ToString("O", CultureInfo.InvariantCulture), summary.TotalDuration.TotalSeconds,
            summary.PrepareLog, summary.EndTime.ToString("O", CultureInfo.InvariantCulture),
            summary.PropertyTableDuration.TotalSeconds);
    }

    private static GeometryMeshPatchSizing PatchSizingFor(ObservableConceptualMeshTopology? topology,
        int[] selectedIDs, string revision, string topologyFingerprint)
    {
        if (topology is null)
            return new GeometryMeshPatchSizing("unavailable", null, "The source geometry topology is unavailable.", selectedIDs, []);
        if (selectedIDs.Length == 0)
            return new GeometryMeshPatchSizing("unavailable", null, "Select one or more source region rows to inspect patch sizing.", selectedIDs, []);

        ObservableConceptualMeshControlPoint[] selected = selectedIDs.Select(fid => FindRegion(topology, fid)).ToArray();
        string[] kinds = selected.Select(point => point.PatchType.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        if (kinds.Length != 1)
            return new GeometryMeshPatchSizing("mixedTypes", null, "The selected regions have different patch types.", selectedIDs, []);
        PatchType patchType = selected[0].PatchType;
        if (patchType != PatchType.Quad)
            return new GeometryMeshPatchSizing("ready", patchType.ToString(), "", selectedIDs, []);

        QuadPatchSideArcResolution northSouth = ResolveQuadPatchSideArcIDs(topology, selectedIDs, "NorthSouth");
        QuadPatchSideArcResolution eastWest = ResolveQuadPatchSideArcIDs(topology, selectedIDs, "EastWest");
        return new GeometryMeshPatchSizing("ready", patchType.ToString(), "", selectedIDs,
        [
            PatchDimension(topology, "NorthSouth", "North / South", northSouth, selectedIDs,
                revision, topologyFingerprint),
            PatchDimension(topology, "EastWest", "East / West", eastWest, selectedIDs,
                revision, topologyFingerprint)
        ]);
    }

    private static GeometryMeshPatchDimension PatchDimension(ObservableConceptualMeshTopology topology,
        string id, string title, QuadPatchSideArcResolution sideArcs, int[] selectedRegionIDs, string revision,
        string topologyFingerprint)
    {
        int[] arcIDs = sideArcs.ArcIDs;
        GeometryMeshPatchSideArcIssue[] issues = sideArcs.Issues;
        GeometryMeshPatchArcSelection sideArcSelection = new(revision, topologyFingerprint,
            [.. selectedRegionIDs], id, [.. arcIDs]);
        if (issues.Length > 0)
            return new GeometryMeshPatchDimension(id, title, arcIDs.Length, null, false, null, false,
                false, false, false, sideArcSelection, issues);
        if (arcIDs.Length == 0)
            return new GeometryMeshPatchDimension(id, title, 0, null, false, null, false, false, false, false,
                sideArcSelection, []);
        ObservableConceptualMeshArc[] arcs = arcIDs.Select(fid => FindArc(topology, fid)).ToArray();
        bool modeMixed = arcs.Select(arc => arc.SizeFunction).Distinct().Skip(1).Any();
        LinearSizeFunction? mode = modeMixed ? null : arcs[0].SizeFunction;
        bool valueMixed = false;
        double? value = null;
        if (mode == LinearSizeFunction.Constant)
        {
            valueMixed = arcs.Select(arc => arc.ConstantSize).Distinct().Skip(1).Any();
            if (!valueMixed) value = arcs[0].ConstantSize;
        }
        else if (mode == LinearSizeFunction.CountWithBias)
        {
            valueMixed = arcs.Select(arc => arc.PointCount).Distinct().Skip(1).Any();
            if (!valueMixed) value = arcs[0].PointCount;
        }
        else valueMixed = modeMixed;
        bool canResize = arcs.All(arc => arc.SizeFunction is LinearSizeFunction.Constant or LinearSizeFunction.CountWithBias);
        bool canApply = mode is LinearSizeFunction.Constant or LinearSizeFunction.CountWithBias;
        return new GeometryMeshPatchDimension(id, title, arcIDs.Length, mode?.ToString(), modeMixed,
            value, valueMixed, canResize, canApply, true, sideArcSelection, []);
    }

    private static GeometryMeshGenerationReport GenerateGeometryMesh(ModelContext context, string? operationID,
        out GeometryMeshDiagnosticHistoryEvent? diagnosticHistoryEvent, ProgressReporter? reporter = null,
        Topology? completedTopology = null, OperationResult? topologyCompletion = null)
    {
        diagnosticHistoryEvent = null;
        var (geometry, topologyView) = GeometryMeshTopologyFor(context);
        if (!geometry.CanMutate) throw new NotSupportedException("This geometry cannot be edited in its current state.");
        if (topologyView is null)
            return new GeometryMeshGenerationReport(false, false, false, null, ["The source conceptual mesh topology is unavailable."]);

        Topology topology = completedTopology ?? topologyView.Topology.Clone();
        OperationResult completion = topologyCompletion ?? topology.Complete();
        if (!completion.Result)
            return GenerationReport(completion, meshWasReplaced: false, mesh: null);

        // Match the original Generate Mesh action: completion happens on a clone
        // before the visible mesh layer is cleared; partial build results are
        // retained as error meshes in the user's in-memory draft.
        geometry.FlowAreaLayer.Clear();
        OperationResult build = topology.BuildConceptualMesh(out _, out Mesh finalMesh,
            new ConceptualMeshCache(), reporter ?? ProgressReporter.None());
        if (reporter?.CancelRequested == true)
            return GenerationReport(build, meshWasReplaced: true, mesh: null);
        if (!string.IsNullOrWhiteSpace(operationID))
            diagnosticHistoryEvent = DiagnosticHistoryEventFor(operationID, build, topologyView);
        bool hasCells = finalMesh is not null && finalMesh.CellCount > 0;
        if (finalMesh is { CellCount: > 0 })
        {
            var flowArea = new FlowArea(finalMesh, FlowAreaLayer.DefaultMeshName)
            {
                HasMeshErrors = !build.Result
            };
            geometry.FlowAreaLayer.Add(flowArea);
        }
        return GenerationReport(build, meshWasReplaced: true, hasCells ? finalMesh : null);
    }

    private static GeometryMeshDiagnosticHistoryEvent DiagnosticHistoryEventFor(string operationID,
        OperationResult result, ObservableConceptualMeshTopology topologyView)
    {
        GeometryTopologyMap map = MapData(topologyView).Map;
        GeometryMeshDiagnosticMessage[] messages = result.Messages.Select((message, messageIndex) =>
        {
            GeometryMeshDiagnosticValue[] values = message.LogData
                .Where(pair => !string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                .Select((pair, valueIndex) => DiagnosticValue(operationID, messageIndex, valueIndex,
                    pair.Key, pair.Value?.ToString() ?? string.Empty, topologyView))
                .ToArray();
            return new GeometryMeshDiagnosticMessage(
                operationID + ":message:" + messageIndex.ToString(CultureInfo.InvariantCulture),
                message.MessageType?.Code ?? string.Empty,
                message.MessageType?.DefaultSeverity.ToString() ?? string.Empty,
                message.EvaluatedMessage,
                values);
        }).ToArray();
        return new GeometryMeshDiagnosticHistoryEvent(operationID, result.Result, messages, map);
    }

    private static GeometryMeshDiagnosticValue DiagnosticValue(string operationID, int messageIndex,
        int valueIndex, string key, string value, ObservableConceptualMeshTopology topologyView)
    {
        string[] selectionIDs = [];
        string? targetFeatureID = null;
        GeometryTopologyCoordinate? coordinate = null;
        Topology topology = topologyView.Topology;

        if (string.Equals(key, "Coordinate", StringComparison.Ordinal))
        {
            Point? point = Point.TryParsePoint(value);
            if (point is { } parsed && double.IsFinite(parsed.X) && double.IsFinite(parsed.Y))
                coordinate = new GeometryTopologyCoordinate(parsed.X, parsed.Y);
        }
        else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 0)
        {
            if (string.Equals(key, "ArcIndex", StringComparison.Ordinal) && index < topologyView.Arcs.Count)
            {
                int fid = topologyView.Arcs[index].Fid;
                targetFeatureID = FeatureID("arc", fid);
                selectionIDs = [targetFeatureID];
            }
            else if (string.Equals(key, "NodeIndex", StringComparison.Ordinal) && index < topologyView.Nodes.Count)
            {
                int fid = topologyView.Nodes[index].Fid;
                targetFeatureID = FeatureID("node", fid);
                selectionIDs = [targetFeatureID];
            }
            else if (string.Equals(key, "ControlPointIndex", StringComparison.Ordinal)
                && index < topologyView.ControlPoints.Count)
            {
                int fid = topologyView.ControlPoints[index].Fid;
                selectionIDs = [FeatureID("region", fid)];
                targetFeatureID = "region-control-point:" + fid.ToString(CultureInfo.InvariantCulture);
            }
            else if (string.Equals(key, "RegionIndex", StringComparison.Ordinal)
                && index < topology.CompletedRegions.Count)
            {
                Region region = topology.CompletedRegions[index];
                HashSet<int> controlPointFids = topologyView.ControlPoints.Select(point => point.Fid).ToHashSet();
                HashSet<int> arcFids = topologyView.Arcs.Select(arc => arc.Fid).ToHashSet();
                selectionIDs = region.ControlPointIndices.Where(controlPointFids.Contains)
                    .Distinct().OrderBy(fid => fid)
                    .Select(fid => FeatureID("region", fid)).ToArray();
                int[] boundaryArcFids = (region.OuterRing?.Arcs ?? [])
                    .Concat((region.InnerRings ?? []).SelectMany(ring => ring.Arcs))
                    .Select(connection => connection.ArcID)
                    .Where(arcFids.Contains).Distinct().OrderBy(fid => fid).ToArray();
                targetFeatureID = "region-boundary:" + string.Join("|", selectionIDs)
                    + ":arcs:" + string.Join(",", boundaryArcFids.Select(fid => fid.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return new GeometryMeshDiagnosticValue(
            operationID + ":message:" + messageIndex.ToString(CultureInfo.InvariantCulture)
                + ":value:" + valueIndex.ToString(CultureInfo.InvariantCulture),
            key, value, selectionIDs, targetFeatureID, coordinate);
    }

    /// <summary>
    /// Stores the exact generated mesh in the opaque parent-save ledger. The
    /// source topology may request Random cell ordering, so replaying
    /// BuildConceptualMesh during save could produce a different preview. The
    /// retained Mesh.Stream format includes cell centers and face-internal
    /// points. Its repaired paired reader restores that same exact artifact.
    /// </summary>
    private static string GeometryMeshArtifactForCurrentFlowAreas(ModelContext context)
    {
        var (geometry, _) = GeometryMeshTopologyFor(context);
        Mesh? mesh = geometry.FlowAreaLayer.Features.Count == 1
            ? geometry.FlowAreaLayer.Features[0].Mesh
            : null;
        if (mesh is null) return string.Empty;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) mesh.Stream(writer);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static void ApplyGeometryMeshArtifact(ModelContext context, string payload,
        GeometryMeshGenerationReport generation)
    {
        var (geometry, _) = GeometryMeshTopologyFor(context);
        if (!geometry.CanMutate) throw new NotSupportedException("This geometry is a result or is currently being updated and cannot be edited.");
        geometry.FlowAreaLayer.Clear();
        if (payload.Length == 0) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(payload); }
        catch (FormatException exception) { throw new ArgumentException("The generated mesh preview payload is invalid.", exception); }
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Mesh mesh = Mesh.Stream(reader);
        if (stream.Position != stream.Length)
            throw new ArgumentException("The generated mesh preview contains trailing data.");
        if (mesh.CellCount == 0 || mesh.Faces.Length == 0 || mesh.FacePoints.Length == 0)
            throw new ArgumentException("The generated mesh preview is incomplete.");

        var flowArea = new FlowArea(mesh, FlowAreaLayer.DefaultMeshName)
        {
            HasMeshErrors = !generation.Succeeded
        };
        geometry.FlowAreaLayer.Add(flowArea);
    }

    private static GeometryMeshGenerationReport GenerationReport(OperationResult result, bool meshWasReplaced, Mesh? mesh) =>
        new(result.Result, !result.Result && mesh is not null, meshWasReplaced,
            mesh?.CellCount, result.Messages.Select(message => message.EvaluatedMessage).ToArray());

    private static string GeometryMeshFlowAreaFingerprint(ModelContext context)
    {
        var (geometry, _) = GeometryMeshTopologyFor(context);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (Observable2DFlowArea flowArea in geometry.FlowAreaLayer.Features)
            {
                writer.Write(flowArea.Name ?? "");
                writer.Write(flowArea.FlowArea.HasMeshErrors);
                Mesh? mesh = flowArea.Mesh;
                writer.Write(mesh is not null);
                if (mesh is not null) mesh.Stream(writer);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static string FlowAreaID(int rowIndex) => "flow-area-row:" + rowIndex.ToString(CultureInfo.InvariantCulture);

    private static Observable2DFlowArea FindFlowArea(ObservableGeometry geometry, string id, string expectedName)
    {
        const string prefix = "flow-area-row:";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(id[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int rowIndex)
            || rowIndex < 0 || rowIndex >= geometry.FlowAreaLayer.Features.Count)
            throw new ArgumentException("The selected flow-area source row no longer exists.");
        Observable2DFlowArea row = geometry.FlowAreaLayer.Features[rowIndex];
        if (!string.Equals(row.Name, expectedName, StringComparison.Ordinal))
            throw new ArgumentException("The selected flow-area source row changed identity. Reload the mesh table before editing it.");
        return row;
    }

    private static ObservableConceptualMeshTopology RequireGeometryMeshTopology(ObservableConceptualMeshTopology? topology) =>
        topology ?? throw new NotSupportedException("The source conceptual mesh topology is unavailable.");

    private static int[] RequireRegionIDs(int[]? featureIDs)
    {
        if (featureIDs is null || featureIDs.Length == 0) throw new ArgumentException("Select at least one source region row.");
        if (featureIDs.Any(fid => fid < 0) || featureIDs.Distinct().Count() != featureIDs.Length)
            throw new ArgumentException("The source region identity list is invalid.");
        // Keep table selection order: the original Triangle/Cartesian quick
        // editor binds its active ControlPoint to the first selected row.
        return [.. featureIDs];
    }

    private static PatchType EnsureOnePatchType(ObservableConceptualMeshTopology topology, int[] featureIDs)
    {
        PatchType[] patchTypes = featureIDs.Select(fid => FindRegion(topology, fid).PatchType).Distinct().ToArray();
        if (patchTypes.Length != 1) throw new InvalidOperationException("The source patch editor does not support mixed patch types.");
        return patchTypes[0];
    }

    private static string RequireQuadAxis(string? axis) => axis is "NorthSouth" or "EastWest"
        ? axis : throw new ArgumentException("Quad patch sizing requires a NorthSouth or EastWest axis.");

    /// <summary>
    /// Reproduces PatchEditor.GetPatchSideDataForRegionAsync: selected Quad
    /// control points contribute their corner-derived sides, and repeated
    /// source arcs are deduplicated before the quick editor mutates them.
    /// </summary>
    private sealed record QuadPatchSideArcResolution(int[] ArcIDs, GeometryMeshPatchSideArcIssue[] Issues);

    private static int[] RequireQuadPatchSideArcIDs(ObservableConceptualMeshTopology topology,
        int[] featureIDs, string axis)
    {
        QuadPatchSideArcResolution resolution = ResolveQuadPatchSideArcIDs(topology, featureIDs, axis);
        if (resolution.Issues.Length > 0)
        {
            string[] details = resolution.Issues.Select(issue =>
                $"Region FID {issue.RegionFID}: {issue.Message}").ToArray();
            throw new InvalidOperationException(string.Join(Environment.NewLine, details));
        }
        return resolution.ArcIDs;
    }

    private static QuadPatchSideArcResolution ResolveQuadPatchSideArcIDs(
        ObservableConceptualMeshTopology topology, int[] featureIDs, string axis)
    {
        var north = new HashSet<int>(); var south = new HashSet<int>();
        var east = new HashSet<int>(); var west = new HashSet<int>();
        var issues = new List<GeometryMeshPatchSideArcIssue>();
        foreach (int fid in featureIDs.Order())
        {
            try
            {
                ObservableConceptualMeshControlPoint point = FindRegion(topology, fid);
                if (point.PatchType != PatchType.Quad) continue;
                if (!topology.Topology.TryGetControlPointContainingRegion(fid, out int regionIndex))
                {
                    issues.Add(SideArcIssue(fid, axis, "regionTopologyUnavailable",
                        "The selected region is not present in a completed source topology."));
                    continue;
                }
                if (regionIndex < 0 || regionIndex >= topology.Topology.CompletedRegions.Count
                    || topology.Topology.CompletedRegions[regionIndex].OuterRing.Arcs.Count < 4)
                {
                    issues.Add(SideArcIssue(fid, axis, "outerRingIncomplete",
                        "The selected source region has fewer than four usable outer-ring arcs."));
                    continue;
                }

                long versionBefore = topology.Topology.VersionXY;
                Topology local = topology.Topology.CloneRegionAsTopology(regionIndex,
                    out _, out _, out Dictionary<int, int> reverseArcIndexMapping, out _);
                _ = local.Complete();
                var result = new OperationResult();
                MeshRegionMetadata data = local.GetRegionData(0, result);
                if (!result.Result)
                {
                    issues.Add(SideArcIssue(fid, axis, "regionMetadataUnavailable",
                        OperationFailureText(result,
                            "The source could not derive patch-side data for this region.")));
                    continue;
                }
                data = local.UpdateCorners(0, data);
                List<ArcWithDirection> arcs = local.CompletedRegions[0].OuterRing.Arcs;
                List<int> northIDs = CollectOriginalArcIDs(arcs, data.CornerRingNodeNE, data.CornerRingNodeNW, reverseArcIndexMapping);
                List<int> southIDs = CollectOriginalArcIDs(arcs, data.CornerRingNodeSW, data.CornerRingNodeSE, reverseArcIndexMapping);
                List<int> eastIDs = CollectOriginalArcIDs(arcs, data.CornerRingNodeSE, data.CornerRingNodeNE, reverseArcIndexMapping);
                List<int> westIDs = CollectOriginalArcIDs(arcs, data.CornerRingNodeNW, data.CornerRingNodeSW, reverseArcIndexMapping);
                double northLength = northIDs.Sum(topology.Topology.GetArcPolylineLength);
                double southLength = southIDs.Sum(topology.Topology.GetArcPolylineLength);
                double eastLength = eastIDs.Sum(topology.Topology.GetArcPolylineLength);
                double westLength = westIDs.Sum(topology.Topology.GetArcPolylineLength);
                if (topology.Topology.VersionXY != versionBefore)
                {
                    issues.Add(SideArcIssue(fid, axis, "topologyChangedDuringLoad",
                        "The source topology changed while patch-side data was being read."));
                    continue;
                }
                if ((northLength > eastLength && northLength > westLength)
                    || (southLength > eastLength && southLength > westLength))
                {
                    (northIDs, eastIDs) = (eastIDs, northIDs);
                    (southIDs, westIDs) = (westIDs, southIDs);
                }
                north.UnionWith(northIDs); south.UnionWith(southIDs);
                east.UnionWith(eastIDs); west.UnionWith(westIDs);
            }
            catch (Exception exception)
            {
                issues.Add(SideArcIssue(fid, axis, "sourceSideDataFailed",
                    string.IsNullOrWhiteSpace(exception.Message)
                        ? "The source patch-side calculation failed."
                        : exception.Message));
            }
        }
        int[] arcIDs = (axis == "NorthSouth" ? north.Concat(south) : east.Concat(west))
            .Distinct().Order().ToArray();
        return new QuadPatchSideArcResolution(arcIDs, [.. issues]);
    }

    private static GeometryMeshPatchSideArcIssue SideArcIssue(int regionFID, string axis,
        string code, string message) => new(regionFID, axis, code, message);

    private static string OperationFailureText(OperationResult result, string fallback)
    {
        string[] messages = result.Messages.Select(message => message.EvaluatedMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        return messages.Length == 0 ? fallback : string.Join(Environment.NewLine, messages);
    }

    private static LinearSizeFunction? UniformSizeFunction(ObservableConceptualMeshTopology topology, int[] arcIDs)
    {
        if (arcIDs.Length == 0) return null;
        LinearSizeFunction first = FindArc(topology, arcIDs[0]).SizeFunction;
        return arcIDs.All(fid => FindArc(topology, fid).SizeFunction == first) ? first : null;
    }

    private static GeometryMeshExtent? MeshExtent(Extent extent) =>
        double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY)
        && double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY)
        && extent.MinX <= extent.MaxX && extent.MinY <= extent.MaxY
            ? new GeometryMeshExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY)
            : null;
}

/// <summary>
/// The parent adapter invokes Prepare on the staged model, then aborts its
/// atomic save when the original source operation reports a failure.
/// </summary>
public sealed class GeometryMeshPrepareException : InvalidOperationException
{
    public GeometrySummary? SourceSummary { get; }

    public GeometryMeshPrepareException(string message, GeometrySummary? sourceSummary)
        : base(message) => SourceSummary = sourceSummary;
}

