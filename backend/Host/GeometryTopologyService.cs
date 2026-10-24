using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Vectors;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Ras.Observables;
using Utility.Logging;
using Utility.Memory;
using Utility.DataStructures;
using Utility.Progress;
using Utility.Validation;
using Visual.Mapping.Observables;
using Visual.Mapping.Observables.EditOperations;
using Visual.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    public static async Task<GeometryTopologySnapshot> LoadGeometryTopologyAsync(GeometryTopologyLoadRequest request)
    {
        VerifyGeometryTopologyModel(request.Model);
        string revision = Revision(request.Model.AssetPath);
        if (request.Revision is not null && !string.Equals(request.Revision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        var state = NewTopologyDraft(request.Model.AssetPath, revision);
        using var context = new ModelContext(ToModelRequest(request.Model));
        var replay = ReplayGeometryTopologyDraft(context, request.Model.AssetPath, revision, state);
        var result = GeometryTopologySnapshotFor(context, request.Model, revision, replay.State, replay.Session);
        if (Revision(request.Model.AssetPath) != revision) throw new SourceFileChangedException();
        return await Task.FromResult(result);
    }

    public static async Task<GeometryTopologySnapshot> EditGeometryTopologyAsync(GeometryTopologyEditRequest request)
    {
        VerifyGeometryTopologyModel(request.Model);
        var state = ReadTopologyDraft(request.DraftState, request.Model.AssetPath, request.Revision);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        GeometryTopologyMutation mutation = request.Mutation ?? throw new ArgumentException("Missing topology edit.");
        if (string.Equals(mutation.Operation, "mapCommand", StringComparison.Ordinal))
        {
            if (mutation.MapCommand is null || request.MapRequestContext is null)
                throw new ArgumentException("A topology map edit requires its typed command and captured map owner.");
            ValidateMapRequestContext(request.MapRequestContext, request.Revision, request.DraftState);
            ValidateMapMutationEnvelope(mutation);
        }
        else if (request.MapRequestContext is not null || mutation.MapCommand is not null)
        {
            throw new ArgumentException("A map owner or map command cannot be attached to an unrelated topology edit.");
        }
        using var context = new ModelContext(ToModelRequest(request.Model));
        GeometryTopologyDraftState edited;
        TopologyCompletionSession completion;
        GeometryTopologyMutation effectiveMutation = mutation;
        if (IsHistoryMutation(mutation))
        {
            edited = ApplyGeometryTopologyHistory(state, mutation);
            var replay = ReplayGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, edited);
            edited = replay.State;
            completion = replay.Session;
        }
        else
        {
            var replay = ReplayGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, state);
            edited = replay.State;
            completion = replay.Session;
            if (mutation.Operation == "mapCommand")
                effectiveMutation = PrepareGeometryTopologyMapMutation(context, request.Model,
                    request.Revision, request.DraftState, request.MapRequestContext!, completion, mutation);
            bool changed = completion.ApplyMutation(effectiveMutation);
            if (changed)
            {
                edited = edited with { Mutations = [.. edited.Mutations, effectiveMutation], RedoMutations = [] };
                edited = RememberRecentTag(edited, effectiveMutation, completion.ObservableTopology);
            }
        }
        var result = GeometryTopologySnapshotFor(context, request.Model, request.Revision, edited, completion) with
        {
            MapRequestContext = request.MapRequestContext
        };
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        return await Task.FromResult(result);
    }

    /// <summary>
    /// Applies a geometry topology draft to the already-open staged model. The
    /// caller owns the one atomic save that includes ordinary geometry edits.
    /// </summary>
    private static void ApplyGeometryTopologyDraft(ModelContext context, string assetPath, string revision, string? topologyState)
    {
        if (string.IsNullOrWhiteSpace(topologyState)) return;
        var state = ReadTopologyDraft(topologyState, assetPath, revision);
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        _ = ReplayGeometryTopologyDraft(context, assetPath, revision, state);
    }

    /// <summary>Standalone topology save for callers that have no ordinary geometry edits.</summary>
    public static async Task<GeometryTopologySnapshot> SaveGeometryTopologyAsync(GeometryTopologySaveRequest request)
    {
        VerifyGeometryTopologyModel(request.Model);
        if (string.IsNullOrWhiteSpace(request.Model.ProjectPath)) throw new ArgumentException("Saving topology requires the owning project.");
        var state = ReadTopologyDraft(request.DraftState, request.Model.AssetPath, request.Revision);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();

        using (var sourceContext = new ModelContext(ToModelRequest(request.Model)))
        {
            var sourceReplay = ReplayGeometryTopologyDraft(sourceContext, request.Model.AssetPath, request.Revision, state);
            if (state.Mutations.Length == 0 && !sourceReplay.Session.HasAutomaticRepairs)
                throw new ArgumentException("There are no topology edits to save.");
        }

        string original = Path.GetFullPath(request.Model.AssetPath);
        string staging = Path.Combine(Path.GetDirectoryName(original)!, ".geometry-topology-edit-" + Guid.NewGuid().ToString("N") + ".pending");
        bool published = false;
        try
        {
            File.Copy(original, staging);
            var stagedModel = ToModelRequest(request.Model);
            using (var context = new ModelContext(stagedModel, staging))
            {
                ApplyGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, request.DraftState);
                context.Layer.Save();
            }

            // Reopen the staged HDF through the real source readers before publishing it.
            using (var verify = new ModelContext(ToModelRequest(request.Model), staging))
            {
                GeometryTopologyDraftState cleanState = NewTopologyDraft(request.Model.AssetPath, request.Revision);
                var reloadedReplay = ReplayGeometryTopologyDraft(verify, request.Model.AssetPath, request.Revision, cleanState);
                var reloaded = GeometryTopologySnapshotFor(verify, request.Model, request.Revision, reloadedReplay.State, reloadedReplay.Session);
                using var draftContext = new ModelContext(ToModelRequest(request.Model));
                var expectedReplay = ReplayGeometryTopologyDraft(draftContext, request.Model.AssetPath, request.Revision, state);
                var expected = GeometryTopologySnapshotFor(draftContext, request.Model, request.Revision, expectedReplay.State, expectedReplay.Session);
                VerifyTopologyRoundTrip(expected, reloaded);
            }
            if (Revision(original) != request.Revision) throw new SourceFileChangedException();
            File.Move(staging, original, overwrite: true);
            published = true;
            return await LoadGeometryTopologyAsync(new GeometryTopologyLoadRequest(request.Model));
        }
        finally
        {
            if (!published && File.Exists(staging)) File.Delete(staging);
        }
    }

    private static ModelRequest ToModelRequest(GeometryTopologyModelRequest model) =>
        new(model.AssetPath, model.Kind, model.ProjectPath, model.ProjectXml,
            CoreLayerContext: model.CoreLayerContext);

    private static void VerifyGeometryTopologyModel(GeometryTopologyModelRequest model)
    {
        if (model.Kind != "Geometry") throw new ArgumentException("Topology editing requires a geometry model.");
        if (string.IsNullOrWhiteSpace(model.AssetPath) || !File.Exists(model.AssetPath)) throw new FileNotFoundException("The geometry source file was not found.", model.AssetPath);
    }

    private static GeometryTopologyDraftState NewTopologyDraft(string assetPath, string revision) =>
        new(SourceFilePaths.Canonical(assetPath), revision, [], [], []);

    private static GeometryTopologyDraftState ReadTopologyDraft(string encoded, string assetPath, string revision)
    {
        if (string.IsNullOrWhiteSpace(encoded)) throw new ArgumentException("Missing topology draft state.");
        GeometryTopologyDraftState state;
        try
        {
            state = JsonSerializer.Deserialize<GeometryTopologyDraftState>(encoded, BackendJson.Options)
                ?? throw new ArgumentException("Invalid topology draft state.");
        }
        catch (JsonException exception) { throw new ArgumentException("Invalid topology draft state.", exception); }
        if (!string.Equals(state.AssetPath, SourceFilePaths.Canonical(assetPath), StringComparison.Ordinal)
            || !string.Equals(state.Revision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (state.Mutations is null || state.RecentArcTags is null || state.RecentRegionTags is null)
            throw new ArgumentException("The topology draft state is incomplete.");
        return state with { RedoMutations = state.RedoMutations ?? [] };
    }

    private static (TopologyCompletionSession Session, GeometryTopologyDraftState State) ReplayGeometryTopologyDraft(
        ModelContext context, string assetPath, string revision, GeometryTopologyDraftState state)
    {
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        var (geometry, observableTopology) = GeometryTopologyFor(context, requireEditable: true);
        if (!geometry.CanMutate) throw new NotSupportedException("This geometry is a result or is currently being updated and cannot be edited.");
        var session = new TopologyCompletionSession(observableTopology);
        session.Initialize();
        GeometryTopologyDraftState replayed = state with
        {
            RecentArcTags = [],
            RecentRegionTags = [],
            RedoMutations = state.RedoMutations ?? []
        };
        foreach (GeometryTopologyMutation mutation in state.Mutations)
        {
            if (mutation is null) throw new ArgumentException("The topology draft contains an empty edit.");
            if (session.ApplyMutation(mutation)) replayed = RememberRecentTag(replayed, mutation, session.ObservableTopology);
        }
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        return (session, replayed);
    }

    private static (ObservableGeometry Geometry, ObservableConceptualMeshTopology Topology) GeometryTopologyFor(ModelContext context, bool requireEditable)
    {
        if (context.Layer is not Geometry || context.Observable is not ObservableGeometry geometry)
            throw new ArgumentException("The source model is not a geometry.");
        var layer = geometry.ConceptualMeshLayer.Layer;
        if (layer.LoadFailed) throw new IOException("The source topology could not be loaded; it was left unchanged.");
        if (geometry.ConceptualMeshLayer.Topology is not ObservableConceptualMeshTopology topology)
            throw new IOException("The source geometry has no conceptual mesh topology.");
        if (requireEditable && !geometry.CanMutate)
            throw new NotSupportedException("This geometry cannot be edited in its current state.");
        return (geometry, topology);
    }

    private static GeometryTopologySnapshot GeometryTopologySnapshotFor(ModelContext context,
        GeometryTopologyModelRequest model, string revision,
        GeometryTopologyDraftState state, TopologyCompletionSession completion)
    {
        var (geometry, topology) = GeometryTopologyFor(context, requireEditable: false);
        bool canEdit = geometry.CanMutate && !geometry.ConceptualMeshLayer.Layer.LoadFailed;
        string draftState = JsonSerializer.Serialize(state, BackendJson.Options);
        string draftIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(draftState))).ToLowerInvariant();
        GeometryTopologyMapBuild mapBuild = MapData(topology);
        GeometryTopologyCompletionResult completionResult = CompletionResult(completion.LatestBuildResult,
            topology, revision, draftIdentity, mapBuild);
        return new GeometryTopologySnapshot(revision, draftState, canEdit,
            state.Mutations.Length > 0 || completion.HasAutomaticRepairs,
            ArcTable(topology), RegionTable(topology), NodeTable(topology),
            topology.ArcTagDefinitions.Select(definition => new GeometryTopologyTagDefinition(definition.Key, definition.Type.ToString())).ToArray(),
            topology.ControlPointTagDefinitions.Select(definition => new GeometryTopologyTagDefinition(definition.Key, definition.Type.ToString())).ToArray(),
            state.RecentArcTags, state.RecentRegionTags, mapBuild.Map, completionResult,
            CanUndo: state.Mutations.Length > 0, CanRedo: state.RedoMutations.Length > 0)
        {
            SmartAddTerrainCapture = CaptureGeometryTopologySmartAddTerrain(context, model, revision)
        };
    }

    private static bool IsHistoryMutation(GeometryTopologyMutation mutation) =>
        mutation.Operation is "undo" or "redo";

    private static GeometryTopologyDraftState ApplyGeometryTopologyHistory(GeometryTopologyDraftState state,
        GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.FeatureKind, "history", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 0
            || mutation.FieldID is not null || mutation.Value is not null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.NodeFeatureIDs is null || mutation.NodeFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null || mutation.MapCommand is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null)
            throw new ArgumentException("A topology history command cannot include feature or field data.");

        if (mutation.Operation == "undo")
        {
            if (state.Mutations.Length == 0) return state;
            GeometryTopologyMutation undone = state.Mutations[^1];
            return state with
            {
                Mutations = state.Mutations[..^1],
                RedoMutations = [.. state.RedoMutations, undone]
            };
        }
        if (mutation.Operation == "redo")
        {
            if (state.RedoMutations.Length == 0) return state;
            GeometryTopologyMutation redone = state.RedoMutations[^1];
            return state with
            {
                Mutations = [.. state.Mutations, redone],
                RedoMutations = state.RedoMutations[..^1]
            };
        }
        throw new ArgumentException("Unsupported topology history command.");
    }

    private static GeometryTopologyCompletionResult CompletionResult(OperationResult operationResult,
        ObservableConceptualMeshTopology topology, string sourceRevision, string draftIdentity,
        GeometryTopologyMapBuild mapBuild)
    {
        if (operationResult is null)
            throw new InvalidOperationException("Retained topology completion returned no OperationResult.");
        GeometryTopologyCompletionMessage[] messages = operationResult.Messages
            .Select((message, ordinal) => new GeometryTopologyCompletionMessage(
                ordinal.ToString(CultureInfo.InvariantCulture) + ":" + message.MessageType.Code,
                message.MessageType.Code,
                message.MessageType.DefaultSeverity.ToString(),
                (message.MessageType as Topology.TopologyMessageClass)?.RegionCompletionSeverity.ToString(),
                message.EvaluatedMessage,
                message.LogData.Select(LogValue).ToArray(),
                DiagnosticTargets(message, topology, mapBuild)))
            .ToArray();
        return new GeometryTopologyCompletionResult(sourceRevision, draftIdentity, operationResult.Result, messages);
    }

    private static GeometryTopologyLogValue LogValue(KeyValuePair<string, object> item)
    {
        object? value = item.Value;
        string type = value?.GetType().FullName ?? "null";
        string text = value switch
        {
            null => string.Empty,
            string stringValue => stringValue,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
        return new GeometryTopologyLogValue(item.Key, text, type, value is int integer ? integer : null);
    }

    private static GeometryTopologyDiagnosticTarget[] DiagnosticTargets(Utility.Logging.Message message,
        ObservableConceptualMeshTopology topology, GeometryTopologyMapBuild mapBuild)
    {
        var targets = new List<GeometryTopologyDiagnosticTarget>();
        var seen = new HashSet<(string FeatureKind, int FID)>();
        foreach (KeyValuePair<string, object> item in message.LogData)
        {
            if (item.Value is not int index) continue;
            switch (item.Key)
            {
                case "ArcIndex" when index >= 0 && index < topology.Arcs.Count:
                {
                    int fid = topology.Arcs[index].Fid;
                    string[] mapIDs = mapBuild.Map.Features
                        .Where(feature => feature.FeatureKind == "arc" && feature.FID == fid && feature.GeometryType == "line")
                        .Select(feature => feature.ID).Distinct(StringComparer.Ordinal).ToArray();
                    Add("arc", fid, FeatureID("arc", fid), mapIDs);
                    break;
                }
                case "NodeIndex" when index >= 0 && index < topology.Nodes.Count:
                {
                    int fid = topology.Nodes[index].Fid;
                    string[] mapIDs = mapBuild.Map.Features
                        .Where(feature => feature.FeatureKind == "node" && feature.FID == fid && feature.GeometryType == "point")
                        .Select(feature => feature.ID).Distinct(StringComparer.Ordinal).ToArray();
                    Add("node", fid, FeatureID("node", fid), mapIDs);
                    break;
                }
                case "RegionIndex" when index >= 0 && index < topology.Topology.CompletedRegions.Count:
                {
                    Region completedRegion = topology.Topology.CompletedRegions[index];
                    string? polygonID = mapBuild.CompletedRegionFeatureIDs.GetValueOrDefault(index);
                    foreach (int controlPointIndex in completedRegion.ControlPointIndices)
                    {
                        // RegionIndex addresses a completed-region ordinal. Resolve
                        // its contained control points to the source feature FIDs;
                        // never expose that ordinal as if it were a source FID.
                        if (controlPointIndex < 0 || controlPointIndex >= topology.ControlPoints.Count) continue;
                        int fid = topology.ControlPoints[controlPointIndex].Fid;
                        var mapIDs = new List<string>();
                        if (polygonID is not null) mapIDs.Add(polygonID);
                        string markerID = "region-control-point:" + fid.ToString(CultureInfo.InvariantCulture);
                        if (mapBuild.Map.Features.Any(feature => feature.ID == markerID && feature.FeatureKind == "region"
                            && feature.FID == fid && feature.GeometryType == "point"))
                            mapIDs.Add(markerID);
                        Add("controlPoint", fid, FeatureID("region", fid), mapIDs.Distinct(StringComparer.Ordinal).ToArray());
                    }
                    break;
                }
            }
        }
        return targets.ToArray();

        void Add(string featureKind, int fid, string rowID, string[] mapIDs)
        {
            if (seen.Add((featureKind, fid))) targets.Add(new GeometryTopologyDiagnosticTarget(featureKind, fid, rowID, mapIDs));
        }
    }

    private static bool IsNoOpTopologyMutation(ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        if (mutation.Operation == "renameMetadataColumn")
            return IsNoOpGeometryTopologyMetadataColumnMutation(topology, mutation);
        if (mutation.Operation == "applyFactor")
        {
            var (arcs, points, factor) = FactorTargets(topology, mutation);
            return arcs.All(arc => EqualityComparer<float>.Default.Equals(arc.ConstantSize * factor, arc.ConstantSize)
                    && Math.Max(1, (int)((float)arc.PointCount / factor)) == arc.PointCount)
                && points.All(point => EqualityComparer<float>.Default.Equals(point.ConstantSize * factor, point.ConstantSize)
                    && EqualityComparer<float>.Default.Equals(point.SecondaryConstantSize * factor, point.SecondaryConstantSize));
        }
        if (mutation.Operation == "applyRecipe")
        {
            ObservableConceptualMeshControlPoint[] points = RecipeTargets(topology, mutation);
            if (points.Length == 0) return true;
            bool mismatch = points.Skip(1).Any(point => !string.Equals(point.Recipe, points[0].Recipe, StringComparison.Ordinal));
            if (mismatch && !mutation.ForceSet)
                throw new ArgumentException("Mixed topology recipes require explicit Force Set.");
            return points.All(point => string.Equals(point.Recipe, mutation.RecipeText, StringComparison.Ordinal));
        }
        if (mutation.Operation == "forceResetField")
        {
            var (points, property) = RegionResetTargets(topology, mutation);
            float first = (float)(property.GetValue(points[0])
                ?? throw new InvalidOperationException("The source reset field has no value."));
            return points.Skip(1).All(point => property.GetValue(point) is float value && value.Equals(first));
        }
        if (mutation.Operation == "forceSetPatchType")
        {
            var (points, patchType) = PatchTypeTargets(topology, mutation);
            return points.All(point => point.PatchType == patchType);
        }
        if (mutation.Operation == "forceSetRegionMultipoint")
        {
            var (points, value) = RegionMultipointTargets(topology, mutation);
            return points.All(point => point.Multipoint == value);
        }
        if (mutation.Operation == "forceSetArcChoice")
        {
            var (arcs, fieldID, value) = ArcChoiceTargets(topology, mutation);
            return arcs.All(arc => string.Equals(ArcChoiceValue(arc, fieldID), value, StringComparison.Ordinal));
        }
        if (mutation.Operation == "forceResetArcBias")
        {
            ObservableConceptualMeshArc[] arcs = ArcBiasResetTargets(topology, mutation);
            return arcs.All(arc => float.IsNaN(arc.Bias));
        }
        if (mutation.Operation == "setRegionInput")
        {
            var (points, property) = RegionEditorInputTargets(topology, mutation);
            string currentText = CommonRegionEditorText(points, property);
            return string.Equals(currentText, mutation.Value, StringComparison.Ordinal);
        }
        if (mutation.Operation is not ("halfSize" or "doubleSize")) return false;
        if (mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 1)
            throw new ArgumentException("A quick size edit requires exactly one selected feature.");
        if (string.Equals(mutation.FeatureKind, "arc", StringComparison.OrdinalIgnoreCase))
        {
            ObservableConceptualMeshArc arc = FindArc(topology, mutation.FeatureIDs[0]);
            return arc.SizeFunction is not (LinearSizeFunction.Constant or LinearSizeFunction.CountWithBias);
        }
        return false;
    }

    private sealed record GeometryTopologyMapBuild(GeometryTopologyMap Map,
        Dictionary<int, string> CompletedRegionFeatureIDs);

    private sealed class TopologyCompletionSession
    {
        private readonly ObservableConceptualMeshTopology _observableTopology;
        private readonly EditStack _editStack;
        private readonly TopologyCompletionContext _completionContext;
        private readonly TopologyEditContext _editContext;

        public Topology Topology => _observableTopology.Topology;
        public ObservableConceptualMeshTopology ObservableTopology => _observableTopology;
        public TopologyEditContext EditContext => _editContext;
        public TopologyCompletionContext CompletionContext => _completionContext;
        public OperationResult LatestBuildResult => _completionContext.LatestBuildResult;
        public bool HasAutomaticRepairs => _editStack.EditStackRecord.Any(record =>
            record.Mode == EditExecutionMode.Execute && record.Operation.IsSilent);

        public TopologyCompletionSession(ObservableConceptualMeshTopology observableTopology)
        {
            _observableTopology = observableTopology;
            _editStack = new EditStack();
            IVectorManager vectorManager = DispatchProxy.Create<IVectorManager, GeometryTopologyHeadlessVectorManagerProxy>();
            _editContext = new TopologyEditContext
            {
                ArcSelectedSet = new SelectedSet(),
                NodeSelectedSet = new SelectedSet(),
                ControlPointSelectedSet = new SelectedSet()
            };
            // These subscriptions mirror TopologyVisualContext.InitializeTopologyEditing.
            // Retained repair operations mutate Topology collections directly, so the
            // observable wrapper must receive the source notifications before the
            // completion context runs automatic add/delete/heal operations.
            _editContext.ArcAdded += observableTopology.AlertNewArc;
            _editContext.ArcInserted += observableTopology.AlertArcInserted;
            _editContext.ArcRemoved += observableTopology.AlertArcAtDeleted;
            _editContext.ControlPointsAdded += observableTopology.AlertNewControlPointsAppended;
            _editContext.ControlPointsInserted += observableTopology.AlertNewControlPointsInserted;
            _editContext.ControlPointsRemoved += observableTopology.AlertControlPointsAtDeleted;
            _editContext.NodeAdded += observableTopology.AlertNewNode;
            _editContext.NodeInserted += observableTopology.AlertNodeInserted;
            _editContext.NodeRemoved += observableTopology.AlertNodeAtDeleted;
            _completionContext = new TopologyCompletionContext(_editContext, _editStack,
                TopologyCompletionContext.GetIsAddingForEditStack(_editStack),
                static (source, buildParams, diagnostics) => source.CompleteAsConceptualMesh(buildParams, diagnostics));
            var editableTopology = new EditableTopology(new Any<Topology>(observableTopology.Topology),
                new OverlayChannels(vectorManager), vectorManager, _completionContext.AttachListenersAndExecute);
            _editContext.AnyEditableTopology = new Any<EditableTopology>(editableTopology);
        }

        public void Initialize()
        {
            if (!_completionContext.TryInitialize())
                throw new InvalidOperationException("Retained topology completion could not initialize.");
        }

        public bool ApplyMutation(GeometryTopologyMutation mutation)
        {
            if (mutation.Operation == "mapCommand")
            {
                if (mutation.MapCommand is null)
                    throw new ArgumentException("A topology map edit is missing its typed command.");
                return ApplyGeometryTopologyMapCommand(_observableTopology, _editContext,
                    _completionContext, mutation.MapCommand);
            }
            if (mutation.Operation == "importLines")
            {
                if (mutation.ImportPayload is null)
                    throw new ArgumentException("A source line/polygon import is missing its replay payload.");
                return ApplyGeometryTopologyImport(_observableTopology, _editContext,
                    _completionContext, mutation.ImportPayload);
            }
            if (mutation.Operation == "deleteSelection")
            {
                DeleteTopologySelectionOperation? deleteOperation = CreateDeleteSelectionOperation(
                    _observableTopology, _editContext, mutation);
                if (deleteOperation is null) return false;
                bool deleteAccepted = _completionContext.AttachListenersAndExecute(deleteOperation);
                if (!deleteAccepted) throw new InvalidOperationException("The topology edit stack rejected source feature deletion.");
                return true;
            }
            if (mutation.Operation == "setArcPointCoordinate")
            {
                EditOperation? coordinateOperation = CreateArcPointCoordinateOperation(_observableTopology, _editContext, mutation);
                if (coordinateOperation is null) return false;
                bool pointAccepted = _completionContext.AttachListenersAndExecute(coordinateOperation);
                if (!pointAccepted) throw new InvalidOperationException("The topology edit stack rejected a source arc-point edit.");
                return true;
            }
            if (IsNoOpTopologyMutation(_observableTopology, mutation)) return false;
            bool accepted = _completionContext.AttachListenersAndExecute(new DraftTopologyMutationOperation(_observableTopology, mutation));
            if (!accepted) throw new InvalidOperationException("The topology edit stack rejected a source mutation.");
            return true;
        }
    }

    /// <summary>
    /// Applies the two source ArcImportDialog operations to the current draft.
    /// The copied projected coordinates are replayed through the same retained
    /// PasteArcOperation or TIN import and Hotswap operations used by the source.
    /// </summary>
    private static bool ApplyGeometryTopologyImport(ObservableConceptualMeshTopology observableTopology,
        TopologyEditContext editContext, TopologyCompletionContext completionContext,
        GeometryTopologyImportPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Mode is not ("Merge" or "NoMerge") || payload.Sources is null || payload.Sources.Length == 0 ||
            payload.Features is null || payload.Features.Length == 0)
            throw new ArgumentException("The geometry import draft is incomplete.");
        if (payload.Sources.Any(source => source is null || source.Family is null ||
                !string.Equals(source.Family.Family, "shapefile", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(source.Path) ||
                string.IsNullOrWhiteSpace(source.Family.SourcePath) ||
                !SourceFilePaths.SameSourceFile(source.Path, source.Family.SourcePath)))
            throw new ArgumentException("The geometry import draft has an invalid Shapefile source identity.");

        Polyline[] lines = payload.Features.Select(feature =>
        {
            if (feature is null || feature.Points is null || feature.Points.Any(point => point is null ||
                    !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new ArgumentException("The geometry import draft contains invalid coordinates.");
            Point[] points = feature.Points.Select(point => new Point(point.X, point.Y)).ToArray();
            return feature.GeometryType switch
            {
                "polyline" => (Polyline)new Polyline(points),
                "polygon" => new Polygon(points),
                _ => throw new ArgumentException("The geometry import draft contains an unsupported feature type.")
            };
        }).ToArray();

        if (payload.Mode == "NoMerge")
        {
            // ImportShapefile -> PasteNoMerge: one PasteArcOperation wrapped in
            // the original multi-operation edit transaction.
            var paste = new PasteArcOperation(editContext, lines, metadataRows: null);
            var operation = new MultiOperationEditOperation(paste);
            if (!completionContext.AttachListenersAndExecute(operation))
                throw new InvalidOperationException("The topology edit stack rejected the source no-merge import.");
            return true;
        }

        Topology sourceTopology = observableTopology.Topology;
        if (!sourceTopology.Lock.TryEnterReadLock(5000))
            throw new TimeoutException("Could not acquire the geometry topology for the source merge import.");
        Topology merged;
        try
        {
            merged = sourceTopology.CloneWithMeshGenerationHooks();
            merged.ImportPolylinesOrPolygons_Tin(lines, new TopologyHelpers.TopologyImportOptions
            {
                ImportArcMetadata = TopologyHelpers.TopologyImportOptions.ArcMetadataImportOption.NoImport
            }, ProgressReporter.None());
            if (payload.MergeContiguousArcs)
                MultiDeleteNodesOperation.MergeCollinearNodes(merged);
            if (payload.RemoveExternalArcs)
            {
                merged.Complete();
                merged.RemoveExternalArcs();
            }
        }
        finally
        {
            sourceTopology.Lock.ExitReadLock();
        }

        // ImportShapefile -> PasteAndMerge: atomically swap the fully processed
        // clone so the retained edit stack can undo/redo the entire import.
        var hotswap = new HotswapTopologyEditOperation(editContext, observableTopology, merged);
        var mergeOperation = new MultiOperationEditOperation(hotswap);
        if (!completionContext.AttachListenersAndExecute(mergeOperation))
            throw new InvalidOperationException("The topology edit stack rejected the source merge import.");
        return true;
    }

    /// <summary>
    /// Replays the source table's Delete Selected action against the retained
    /// topology edit stack. The wire carries stable source FIDs; the original
    /// operation consumes collection indices, so resolve every identity against
    /// the current replayed source collections before populating its selection.
    /// </summary>
    private static DeleteTopologySelectionOperation? CreateDeleteSelectionOperation(
        ObservableConceptualMeshTopology topology, TopologyEditContext editContext,
        GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.Operation, "deleteSelection", StringComparison.Ordinal)
            || mutation.FeatureIDs is null
            || mutation.ArcFeatureIDs is null || mutation.ControlPointFeatureIDs is null
            || mutation.NodeFeatureIDs is null
            || mutation.FieldID is not null || mutation.Value is not null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null)
            throw new ArgumentException("A topology delete command must contain only selected source feature identities.");

        editContext.ArcSelectedSet.Clear();
        editContext.NodeSelectedSet.Clear();
        editContext.ControlPointSelectedSet.Clear();

        if (string.Equals(mutation.FeatureKind, "selection", StringComparison.Ordinal))
        {
            if (mutation.FeatureIDs.Length != 0)
                throw new ArgumentException("A typed topology selection must not include untyped feature identities.");
            ValidateDeleteFeatureIDs(mutation.ArcFeatureIDs, "arc", allowEmpty: true);
            ValidateDeleteFeatureIDs(mutation.ControlPointFeatureIDs, "control point", allowEmpty: true);
            ValidateDeleteFeatureIDs(mutation.NodeFeatureIDs, "node", allowEmpty: true);
            if (mutation.ArcFeatureIDs.Length == 0 && mutation.ControlPointFeatureIDs.Length == 0
                && mutation.NodeFeatureIDs.Length == 0)
                throw new ArgumentException("A topology delete command requires a selected source feature.");
            foreach (int index in ResolveSourceFeatureIndices(topology.Arcs, mutation.ArcFeatureIDs,
                             arc => arc.Fid, "arc"))
                editContext.ArcSelectedSet.Add(index);
            foreach (int index in ResolveSourceFeatureIndices(topology.ControlPoints, mutation.ControlPointFeatureIDs,
                             controlPoint => controlPoint.Fid, "control point"))
                editContext.ControlPointSelectedSet.Add(index);
            foreach (int index in ResolveSourceFeatureIndices(topology.Nodes, mutation.NodeFeatureIDs,
                             node => node.Fid, "node"))
                editContext.NodeSelectedSet.Add(index);
        }
        else
        {
            if (mutation.ArcFeatureIDs.Length != 0 || mutation.ControlPointFeatureIDs.Length != 0
                || mutation.NodeFeatureIDs.Length != 0)
                throw new ArgumentException("A single-kind topology delete cannot include additional feature collections.");
            ValidateDeleteFeatureIDs(mutation.FeatureIDs, mutation.FeatureKind);
            switch (mutation.FeatureKind)
            {
                case "arc":
                    foreach (int index in ResolveSourceFeatureIndices(topology.Arcs, mutation.FeatureIDs,
                                 arc => arc.Fid, "arc"))
                        editContext.ArcSelectedSet.Add(index);
                    break;
                case "node":
                    foreach (int index in ResolveSourceFeatureIndices(topology.Nodes, mutation.FeatureIDs,
                                 node => node.Fid, "node"))
                        editContext.NodeSelectedSet.Add(index);
                    break;
                // The original Regions table edits ControlPoints and binds its
                // FeatureType as ControlPoint. Preserve that source operation's
                // control-point deletion algorithm, including boundary repair.
                case "region":
                    foreach (int index in ResolveSourceFeatureIndices(topology.ControlPoints, mutation.FeatureIDs,
                             controlPoint => controlPoint.Fid, "control point"))
                    editContext.ControlPointSelectedSet.Add(index);
                    break;
                default:
                    throw new ArgumentException("Unsupported topology feature kind for deletion.");
            }
        }

        return new DeleteTopologySelectionOperation(editContext, null);
    }

    private static void ValidateDeleteFeatureIDs(int[] featureIDs, string sourceName, bool allowEmpty = false)
    {
        if (featureIDs.Length == 0 && allowEmpty) return;
        if (featureIDs.Length == 0 || featureIDs.Any(fid => fid < 0)
            || featureIDs.Distinct().Count() != featureIDs.Length)
            throw new ArgumentException("The selected " + sourceName + " identities are invalid or duplicated.");
    }

    private static int[] ResolveSourceFeatureIndices<T>(IEnumerable<T> rows, int[] requestedFIDs,
        Func<T, int> featureID, string sourceName)
    {
        var unresolved = requestedFIDs.ToHashSet();
        var indices = new List<int>(requestedFIDs.Length);
        int index = 0;
        foreach (T row in rows)
        {
            if (unresolved.Remove(featureID(row))) indices.Add(index);
            index++;
        }
        if (unresolved.Count != 0)
            throw new ArgumentException("A selected " + sourceName + " no longer exists in the current topology draft.");
        return indices.ToArray();
    }

    private static EditOperation? CreateArcPointCoordinateOperation(ObservableConceptualMeshTopology topology,
        TopologyEditContext editContext, GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.Operation, "setArcPointCoordinate", StringComparison.Ordinal)
            || !string.Equals(mutation.FeatureKind, "arc", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 1
            || mutation.FieldID is not null || mutation.Value is not null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.RecipeText is not null || mutation.ForceSet)
            throw new ArgumentException("An arc-point coordinate command has an invalid shape.");

        ObservableConceptualMeshArc feature = FindArc(topology, mutation.FeatureIDs[0]);
        if (mutation.Axis is not ("X" or "Y") || mutation.RawValue is null
            || !double.TryParse(mutation.RawValue, out double coordinate) || !double.IsFinite(coordinate))
            throw new ArgumentException("The selected arc point coordinate is invalid.");

        Arc arc = topology.Topology.Arcs[feature.Fid];
        Point current;
        if (mutation.PointKind == "start")
        {
            if (mutation.InternalPointIndex is not null || mutation.EndpointNodeIndex != arc.StartNode)
                throw new ArgumentException("The selected arc point is no longer available.");
            current = topology.Topology.Nodes[arc.StartNode].Point;
            Point next = mutation.Axis == "X" ? new Point(coordinate, current.Y) : new Point(current.X, coordinate);
            if (current.X == next.X && current.Y == next.Y) return null;
            return new SetNodePointOperation(editContext, arc.StartNode, next);
        }
        if (mutation.PointKind == "end")
        {
            if (mutation.InternalPointIndex is not null || mutation.EndpointNodeIndex != arc.EndNode)
                throw new ArgumentException("The selected arc point is no longer available.");
            current = topology.Topology.Nodes[arc.EndNode].Point;
            Point next = mutation.Axis == "X" ? new Point(coordinate, current.Y) : new Point(current.X, coordinate);
            if (current.X == next.X && current.Y == next.Y) return null;
            return new SetNodePointOperation(editContext, arc.EndNode, next);
        }
        if (mutation.PointKind == "internal")
        {
            if (mutation.EndpointNodeIndex is not null || mutation.InternalPointIndex is not int internalIndex
                || internalIndex < 0 || internalIndex >= arc.InternalPointCount)
                throw new ArgumentException("The selected arc point is no longer available.");
            current = arc.InternalPoints[internalIndex];
            Point next = mutation.Axis == "X" ? new Point(coordinate, current.Y) : new Point(current.X, coordinate);
            if (current.X == next.X && current.Y == next.Y) return null;
            return new SetArcInternalPointOperation(editContext, feature.Fid, internalIndex, next);
        }
        throw new ArgumentException("The selected arc point is no longer available.");
    }

    private sealed class DraftTopologyMutationOperation : EditOperation
    {
        private readonly ObservableConceptualMeshTopology _topology;
        private readonly GeometryTopologyMutation _mutation;

        public override string Description => "Host topology draft: " + _mutation.Operation;

        public DraftTopologyMutationOperation(ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
            : base(null)
        {
            _topology = topology;
            _mutation = mutation;
        }

        protected override bool ApplyInternal() => ApplyGeometryTopologyMutation(_topology, _mutation);

        protected override bool UndoInternal() => throw new NotSupportedException("Topology draft history is replayed from its source baseline.");
    }

    private static GeometryTopologyFeatureTable ArcTable(ObservableConceptualMeshTopology topology)
    {
        GeometryTopologyField[] fields =
        [
            TopologyField("SizeFunction", "LinearSizeFunction", ["Constant", "CountWithBias", "Unset"]),
            TopologyField("PointCount", "int"), TopologyField("Bias", "float"), TopologyField("ConstantSize", "float"),
            TopologyField("ConstrainStretching", "ConstrainStretchingOptions", ["Default", "Constrained", "Floating"]),
            TopologyField("LaplaceBC", "LaplaceBCOptions", ["Default", "ZeroFlux", "Dirichlet"]),
            TopologyField("DistanceStraightLine", "bool"), TopologyField("AreaWeight", "float"),
            TopologyField("DiscretizeMagnetism", "float"), TopologyField("FollowExactly", "bool")
        ];
        var columns = new List<GeometryTopologyColumn>
        {
            new("FID", "FID", "int"), new("StartNodeID", "Start Node", "int"),
            new("EndNodeID", "End Node", "int"), new("Length", "Length", "double")
        };
        columns.AddRange(fields.Select(field => new GeometryTopologyColumn(field.ID, field.Title, field.SourceType)));
        columns.AddRange(MetadataColumns(topology.ArcMetadata, topology, MeshExtensions.IsUserDefinedArcColumn)
            .Where(column => fields.All(field => field.ID != column.ID)));
        var rows = topology.Arcs.Select(arc =>
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FID"] = arc.Fid.ToString(CultureInfo.InvariantCulture),
                ["StartNodeID"] = arc.Arc.StartNode.ToString(CultureInfo.InvariantCulture),
                ["EndNodeID"] = arc.Arc.EndNode.ToString(CultureInfo.InvariantCulture),
                ["Length"] = Format(arc.Length),
                ["SizeFunction"] = arc.SizeFunction.ToString(),
                ["PointCount"] = arc.PointCount.ToString(CultureInfo.InvariantCulture),
                ["Bias"] = Format(arc.Bias), ["ConstantSize"] = Format(arc.ConstantSize),
                ["ConstrainStretching"] = arc.ConstrainStretching.ToString(), ["LaplaceBC"] = arc.LaplaceBC.ToString(),
                ["DistanceStraightLine"] = Format(arc.DistanceStraightLine), ["AreaWeight"] = Format(arc.AreaWeight),
                ["DiscretizeMagnetism"] = Format(arc.DiscretizeMagnetism), ["FollowExactly"] = Format(arc.FollowExactly)
            };
            AppendMetadata(values, arc.TableRow, topology.ArcMetadata);
            return new GeometryTopologyFeatureRow(FeatureID("arc", arc.Fid), arc.Fid, values,
                StartNodeID: arc.Arc.StartNode, EndNodeID: arc.Arc.EndNode);
        }).ToArray();
        return new GeometryTopologyFeatureTable("arc", columns.ToArray(), fields, rows);
    }

    private static GeometryTopologyFeatureTable RegionTable(ObservableConceptualMeshTopology topology)
    {
        GeometryTopologyField[] fields =
        [
            TopologyField("PatchType", "PatchType", ["Quad", "TrianglePaver", "Cartesian", "OneD", "Empty"]),
            TopologyField("ConstantSize", "float"), TopologyField("SecondaryConstantSize", "float"), TopologyField("RotationDegrees", "float"),
            TopologyField("BiasInternal", "float"), TopologyField("Multipoint", "bool"), TopologyField("TinResolution", "float"),
            TopologyField("SizeTolerance", "float"), TopologyField("Recipe", "string")
        ];
        var columns = new List<GeometryTopologyColumn>
        {
            new("FID", "FID", "int"), new("X", "X", "double"), new("Y", "Y", "double")
        };
        columns.AddRange(fields.Select(field => new GeometryTopologyColumn(field.ID, field.Title, field.SourceType)));
        columns.AddRange(MetadataColumns(topology.ControlPointMetadata, topology, MeshExtensions.IsUserDefinedControlPointColumn)
            .Where(column => fields.All(field => field.ID != column.ID)));
        var rows = topology.ControlPoints.Select(point =>
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FID"] = point.Fid.ToString(CultureInfo.InvariantCulture),
                ["X"] = Format(point.ControlPoint.X), ["Y"] = Format(point.ControlPoint.Y),
                ["PatchType"] = point.PatchType.ToString(), ["ConstantSize"] = Format(point.ConstantSize),
                ["SecondaryConstantSize"] = Format(point.SecondaryConstantSize), ["RotationDegrees"] = Format(point.RotationDegrees),
                ["BiasInternal"] = Format(point.BiasInternal), ["Multipoint"] = Format(point.Multipoint),
                ["TinResolution"] = Format(point.TinResolution), ["SizeTolerance"] = Format(point.SizeTolerance),
                ["Recipe"] = point.Recipe
            };
            AppendMetadata(values, point.TableRow, topology.ControlPointMetadata);
            return new GeometryTopologyFeatureRow(FeatureID("region", point.Fid), point.Fid, values,
                point.ControlPoint.X, point.ControlPoint.Y);
        }).ToArray();
        return new GeometryTopologyFeatureTable("region", columns.ToArray(), fields, rows);
    }

    private static GeometryTopologyFeatureTable NodeTable(ObservableConceptualMeshTopology topology)
    {
        GeometryTopologyColumn[] columns = new GeometryTopologyColumn[] { new("FID", "FID", "int"), new("X", "X", "double"), new("Y", "Y", "double") }
            .Concat(MetadataColumns(topology.NodeMetadata)).ToArray();
        var rows = topology.Nodes.Select(node =>
        {
            var point = node.Node.Point;
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FID"] = node.Fid.ToString(CultureInfo.InvariantCulture), ["X"] = Format(point.X), ["Y"] = Format(point.Y)
            };
            AppendMetadata(values, node.TableRow, topology.NodeMetadata);
            return new GeometryTopologyFeatureRow(FeatureID("node", node.Fid), node.Fid, values, point.X, point.Y);
        }).ToArray();
        return new GeometryTopologyFeatureTable("node", columns, [], rows);
    }

    private static GeometryTopologyMapBuild MapData(ObservableConceptualMeshTopology observableTopology)
    {
        Topology topology = observableTopology.Topology;
        var features = new List<GeometryTopologyMapFeature>();
        var completedRegionFeatureIDs = new Dictionary<int, string>();
        HashSet<int> sourceArcFids = observableTopology.Arcs.Select(arc => arc.Fid).ToHashSet();
        HashSet<int> sourceControlPointFids = observableTopology.ControlPoints.Select(point => point.Fid).ToHashSet();

        foreach (ObservableConceptualMeshArc sourceArc in observableTopology.Arcs)
        {
            int fid = sourceArc.Fid;
            if (fid < 0 || fid >= topology.ArcCount) continue;
            GeometryTopologyCoordinate[] path = Coordinates(topology.GetArcPolyline(fid));
            if (path.Length < 2) continue;
            string rowID = FeatureID("arc", fid);
            features.Add(new GeometryTopologyMapFeature(rowID, "arc", fid, "line", [rowID], [path]));
        }

        for (int regionIndex = 0; regionIndex < topology.CompletedRegions.Count; regionIndex++)
        {
            Region region = topology.CompletedRegions[regionIndex];
            if (region.OuterRing is null) continue;
            GeometryTopologyCoordinate[] outer = RingCoordinates(topology, region.OuterRing);
            if (outer.Length < 3) continue;
            var paths = new List<GeometryTopologyCoordinate[]> { outer };
            bool ringsAreComplete = true;
            if (region.InnerRings is not null)
            {
                foreach (RegionRing innerRing in region.InnerRings)
                {
                    GeometryTopologyCoordinate[] inner = RingCoordinates(topology, innerRing);
                    if (inner.Length < 3)
                    {
                        ringsAreComplete = false;
                        break;
                    }
                    paths.Add(inner);
                }
            }
            if (!ringsAreComplete) continue;

            int[] selectionFids = region.ControlPointIndices
                .Where(sourceControlPointFids.Contains)
                .Distinct()
                .OrderBy(fid => fid)
                .ToArray();
            string[] selectionIDs = selectionFids.Select(fid => FeatureID("region", fid)).ToArray();
            int[] boundaryArcFids = region.OuterRing.Arcs
                .Concat((region.InnerRings ?? []).SelectMany(ring => ring.Arcs))
                .Select(connection => connection.ArcID)
                .Where(sourceArcFids.Contains)
                .Distinct()
                .OrderBy(fid => fid)
                .ToArray();
            string stableID = "region-boundary:" + string.Join("|", selectionIDs)
                + ":arcs:" + string.Join(",", boundaryArcFids.Select(fid => fid.ToString(CultureInfo.InvariantCulture)));
            int? sourceFID = selectionFids.Length == 1
                ? selectionFids[0]
                : null;
            features.Add(new GeometryTopologyMapFeature(stableID, "region", sourceFID, "polygon", selectionIDs, paths.ToArray()));
            completedRegionFeatureIDs[regionIndex] = stableID;
        }

        // The Regions table edits source control points. Keep their actual source
        // coordinates visible and individually selectable, including points that
        // are not currently part of a completed region.
        foreach (ObservableConceptualMeshControlPoint controlPoint in observableTopology.ControlPoints)
        {
            Point sourcePoint = controlPoint.ControlPoint;
            if (!IsFinite(sourcePoint)) continue;
            string rowID = FeatureID("region", controlPoint.Fid);
            features.Add(new GeometryTopologyMapFeature("region-control-point:" + controlPoint.Fid.ToString(CultureInfo.InvariantCulture),
                "region", controlPoint.Fid, "point", [rowID], [[Coordinate(sourcePoint)]]));
        }

        foreach (ObservableNode node in observableTopology.Nodes)
        {
            Point sourcePoint = node.Node.Point;
            if (!IsFinite(sourcePoint)) continue;
            string rowID = FeatureID("node", node.Fid);
            GeometryTopologyIncidentArc[] incidentArcs = node.Node.Arcs
                .Select(connection => new GeometryTopologyIncidentArc(connection.ArcID,
                    topology.GetArcPolyline(connection.ArcID).Length))
                .ToArray();
            features.Add(new GeometryTopologyMapFeature(rowID, "node", node.Fid, "point", [rowID],
                [[Coordinate(sourcePoint)]], incidentArcs));
        }

        GeometryTopologyCoordinate[] allCoordinates = features.SelectMany(feature => feature.Paths)
            .SelectMany(path => path).ToArray();
        GeometryTopologyExtent? extent = allCoordinates.Length == 0 ? null : new GeometryTopologyExtent(
            allCoordinates.Min(point => point.X), allCoordinates.Min(point => point.Y),
            allCoordinates.Max(point => point.X), allCoordinates.Max(point => point.Y));
        return new GeometryTopologyMapBuild(new GeometryTopologyMap(extent, features.ToArray()), completedRegionFeatureIDs);
    }

    private static GeometryTopologyCoordinate[] Coordinates(Polyline? polyline)
    {
        if (polyline is null || polyline.Count < 2) return [];
        var coordinates = new GeometryTopologyCoordinate[polyline.Count];
        for (int index = 0; index < polyline.Count; index++)
        {
            Point point = polyline[index];
            if (!IsFinite(point)) return [];
            coordinates[index] = Coordinate(point);
        }
        return coordinates;
    }

    private static GeometryTopologyCoordinate[] RingCoordinates(Topology topology, RegionRing ring)
    {
        if (ring is null || ring.Arcs is null) return [];
        var coordinates = new List<GeometryTopologyCoordinate>();
        foreach (ArcWithDirection connection in ring.Arcs)
        {
            if (connection.ArcID < 0 || connection.ArcID >= topology.ArcCount) return [];
            Polyline? arc = topology.GetArcPolyline(connection.ArcID);
            if (arc is null || arc.Count < 2) return [];

            int firstIndex = connection.IsForward ? 0 : arc.Count - 1;
            GeometryTopologyCoordinate segmentStart = Coordinate(arc[firstIndex]);
            if (!IsFinite(arc[firstIndex])) return [];
            if (coordinates.Count > 0)
            {
                GeometryTopologyCoordinate previous = coordinates[^1];
                if (!SameCoordinate(previous, segmentStart)) return [];
            }

            for (int offset = 0; offset < arc.Count; offset++)
            {
                int index = connection.IsForward ? offset : arc.Count - offset - 1;
                Point point = arc[index];
                if (!IsFinite(point)) return [];
                GeometryTopologyCoordinate coordinate = Coordinate(point);
                if (coordinates.Count > 0 && offset == 0) continue;
                coordinates.Add(coordinate);
            }
        }

        if (coordinates.Count < 3 || !SameCoordinate(coordinates[0], coordinates[^1])) return [];
        return coordinates.ToArray();
    }

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static GeometryTopologyCoordinate Coordinate(Point point) => new(point.X, point.Y);

    private static bool SameCoordinate(GeometryTopologyCoordinate left, GeometryTopologyCoordinate right) =>
        left.X == right.X && left.Y == right.Y;

    private static GeometryTopologyField TopologyField(string id, string sourceType, string[]? choices = null) =>
        new(id, id, sourceType, choices ?? []);

    private static string FeatureID(string kind, int fid) => kind + ":" + fid.ToString(CultureInfo.InvariantCulture);

    private static GeometryTopologyColumn[] MetadataColumns(ObservableTable table, ObservableConceptualMeshTopology? topology = null,
        Func<string, bool>? isUserDefined = null) => table.Columns
        .Select(column => new GeometryTopologyColumn(column.Name, column.Name, SourceTypeName(column.Type), column.IsReadOnly,
            IsTag: topology?.IsReservedColumn(column.Name) != true,
            IsUserDefined: isUserDefined?.Invoke(column.Name) == true)).ToArray();

    private static string SourceTypeName(Type type) => type == typeof(string) ? "string"
        : type == typeof(bool) ? "bool" : type == typeof(int) ? "int"
        : type == typeof(float) ? "float" : type == typeof(double) ? "double"
        : type.IsEnum ? type.Name : type.Name;

    private static void AppendMetadata(Dictionary<string, string> values, TableRow row, ObservableTable table)
    {
        foreach (TableColumn column in table.Columns)
            if (!values.ContainsKey(column.Name)) values[column.Name] = Format(row.Value(column.Name));
    }

    private static bool ApplyGeometryTopologyMutation(ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        if (mutation.FeatureIDs is null) throw new ArgumentException("Feature identities are required.");
        string kind = mutation.FeatureKind.ToLowerInvariant();
        switch (mutation.Operation)
        {
            case "applyFactor":
            {
                var (arcs, points, factor) = FactorTargets(topology, mutation);
                // This is the retained TopologyPropertiesVM.ApplyFactor formula.
                // Validate all FIDs before mutating any observable row so an
                // invalid request never publishes a partially changed draft.
                foreach (ObservableConceptualMeshControlPoint point in points)
                {
                    point.ConstantSize *= factor;
                    point.SecondaryConstantSize *= factor;
                }
                foreach (ObservableConceptualMeshArc arc in arcs)
                {
                    arc.ConstantSize *= factor;
                    arc.PointCount = Math.Max(1, (int)((float)arc.PointCount / factor));
                }
                return arcs.Length > 0 || points.Length > 0;
            }
            case "applyRecipe":
            {
                ObservableConceptualMeshControlPoint[] points = RecipeTargets(topology, mutation);
                if (points.Length == 0) return false;
                bool mismatch = points.Skip(1).Any(point => !string.Equals(point.Recipe, points[0].Recipe, StringComparison.Ordinal));
                if (mismatch && !mutation.ForceSet)
                    throw new ArgumentException("Mixed topology recipes require explicit Force Set.");
                string rawRecipe = mutation.RecipeText ?? throw new ArgumentException("A topology recipe string is required.");
                if (points.All(point => string.Equals(point.Recipe, rawRecipe, StringComparison.Ordinal))) return false;
                foreach (ObservableConceptualMeshControlPoint point in points) point.Recipe = rawRecipe;
                return true;
            }
            case "setField":
                if (mutation.FeatureIDs.Length != 1 || mutation.FieldID is null || mutation.Value is null)
                    throw new ArgumentException("A property edit requires one source feature, a field and a value.");
                if (kind == "arc") SetArcField(FindArc(topology, mutation.FeatureIDs[0]), mutation.FieldID, mutation.Value);
                else if (kind == "region") SetRegionField(FindRegion(topology, mutation.FeatureIDs[0]), mutation.FieldID, mutation.Value);
                else throw new ArgumentException("Only arc and region properties can be edited.");
                return true;
            case "setRegionInput":
            {
                var (points, property) = RegionEditorInputTargets(topology, mutation);
                string text = mutation.Value ?? throw new ArgumentException("A source property text value is required.");
                float value = float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out float parsed)
                    ? parsed : float.NaN;
                foreach (ObservableConceptualMeshControlPoint point in points)
                    property.SetValue(point, value);
                if (mutation.FieldID == "ConstantSize")
                    foreach (ObservableConceptualMeshControlPoint point in points)
                        PushRegionSizingToOuterArcs(topology, point);
                return points.Length > 0;
            }
            case "forceResetField":
                return ForceResetRegionField(topology, mutation);
            case "forceSetPatchType":
            {
                var (points, patchType) = PatchTypeTargets(topology, mutation);
                foreach (ObservableConceptualMeshControlPoint point in points)
                    point.PatchType = patchType;
                return points.Length > 0;
            }
            case "forceSetRegionMultipoint":
            {
                var (points, value) = RegionMultipointTargets(topology, mutation);
                foreach (ObservableConceptualMeshControlPoint point in points)
                    point.Multipoint = value;
                return points.Length > 0;
            }
            case "forceSetArcChoice":
            {
                var (arcs, fieldID, value) = ArcChoiceTargets(topology, mutation);
                foreach (ObservableConceptualMeshArc arc in arcs)
                    SetArcField(arc, fieldID, value);
                return arcs.Length > 0;
            }
            case "forceResetArcBias":
            {
                ObservableConceptualMeshArc[] arcs = ArcBiasResetTargets(topology, mutation);
                foreach (ObservableConceptualMeshArc arc in arcs)
                    arc.Bias = float.NaN;
                return arcs.Length > 0;
            }
            case "halfSize":
            case "doubleSize":
                if (mutation.FeatureIDs.Length != 1) throw new ArgumentException("A quick size edit requires exactly one selected feature.");
                float ratio = mutation.Operation == "halfSize" ? 0.5f : 2f;
                if (kind == "arc")
                {
                    ObservableConceptualMeshArc arc = FindArc(topology, mutation.FeatureIDs[0]);
                    if (arc.SizeFunction == LinearSizeFunction.Constant)
                        SetValidatedProperty(arc, "ConstantSize", arc.ConstantSize * ratio);
                    else if (arc.SizeFunction == LinearSizeFunction.CountWithBias)
                        SetValidatedProperty(arc, "PointCount", (int)((float)arc.PointCount * ratio));
                    else return false; // Source Resize is intentionally a no-op for Unset.
                }
                else if (kind == "region")
                {
                    ObservableConceptualMeshControlPoint point = FindRegion(topology, mutation.FeatureIDs[0]);
                    if (point.PatchType is PatchType.TrianglePaver or PatchType.Cartesian)
                    {
                        if (mutation.FieldID is not null) throw new ArgumentException("Triangle and Cartesian quick sizing do not use an axis.");
                        SetValidatedProperty(point, "ConstantSize", point.ConstantSize * ratio);
                        SetValidatedProperty(point, "SecondaryConstantSize", point.SecondaryConstantSize * ratio);
                        PushRegionSizingToOuterArcs(topology, point);
                    }
                    else if (point.PatchType == PatchType.Quad)
                    {
                        if (mutation.FieldID is not ("NorthSouth" or "EastWest"))
                            throw new ArgumentException("Quad quick sizing requires a NorthSouth or EastWest axis.");
                        ResizeQuadPatchSide(topology, point, mutation.FieldID, ratio);
                    }
                    else throw new NotSupportedException("The source editor does not define a scalar quick-size operation for this patch type.");
                }
                else throw new ArgumentException("Size adjustment requires an arc or region.");
                return true;
            case "createTag":
                CreateTag(topology, kind, mutation.TagName, mutation.TagType);
                return true;
            case "addMetadataColumn":
            case "renameMetadataColumn":
            case "deleteMetadataColumn":
                return ApplyGeometryTopologyMetadataColumnMutation(topology, mutation);
            case "setTag":
            case "removeTag":
                SetTagValues(topology, kind, mutation.FeatureIDs, mutation.TagName, mutation.Operation == "removeTag" ? null : mutation.TagValue);
                return true;
            default: throw new ArgumentException("Unsupported topology mutation: " + mutation.Operation);
        }
    }

    private static (ObservableConceptualMeshArc[] Arcs, ObservableConceptualMeshControlPoint[] Points, float Factor)
        FactorTargets(ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.FeatureKind, "topology", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 0
            || mutation.FieldID is not null || mutation.Value is not null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ArcFeatureIDs is null || mutation.ControlPointFeatureIDs is null
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null)
            throw new ArgumentException("A topology factor command has an invalid shape.");
        if (mutation.FactorText is null || !float.TryParse(mutation.FactorText, out float factor))
            throw new ArgumentException("The topology factor is invalid.");

        if (!mutation.ApplyToSelectedOnly)
            return (topology.Arcs.ToArray(), topology.ControlPoints.ToArray(), factor);

        int[] arcFIDs = UniqueFeatureIDs(mutation.ArcFeatureIDs, "arc");
        int[] controlPointFIDs = UniqueFeatureIDs(mutation.ControlPointFeatureIDs, "control point");
        return (arcFIDs.Select(fid => FindArc(topology, fid)).ToArray(),
            controlPointFIDs.Select(fid => FindRegion(topology, fid)).ToArray(), factor);
    }

    private static ObservableConceptualMeshControlPoint[] RecipeTargets(ObservableConceptualMeshTopology topology,
        GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.FeatureKind, "region", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 0
            || mutation.FieldID is not null || mutation.Value is not null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ControlPointFeatureIDs is null || mutation.ArcFeatureIDs is null
            || mutation.ArcFeatureIDs.Length != 0 || mutation.FactorText is not null
            || mutation.ApplyToSelectedOnly || mutation.PointKind is not null
            || mutation.InternalPointIndex is not null || mutation.EndpointNodeIndex is not null
            || mutation.Axis is not null || mutation.RawValue is not null
            || mutation.RecipeText is null)
            throw new ArgumentException("A topology recipe command has an invalid shape.");
        int[] fids = UniqueFeatureIDs(mutation.ControlPointFeatureIDs, "control point");
        return fids.Select(fid => FindRegion(topology, fid)).ToArray();
    }

    private static int[] UniqueFeatureIDs(int[] fids, string kind)
    {
        if (fids.Any(fid => fid < 0) || fids.Distinct().Count() != fids.Length)
            throw new ArgumentException("A selected topology " + kind + " identity is invalid or duplicated.");
        return fids;
    }

    /// <summary>
    /// Mirrors TrianglePatchQuickEditor/CartesianPatchQuickEditor property
    /// listeners: when the
    /// original application enables auto-population, a constant-size change
    /// also updates eligible outer-ring arcs. Interior arcs and arcs using a
    /// non-constant size function are left alone.
    /// </summary>
    private static void PushRegionSizingToOuterArcs(ObservableConceptualMeshTopology topology,
        ObservableConceptualMeshControlPoint point)
    {
        if (point.PatchType is not (PatchType.TrianglePaver or PatchType.Cartesian)) return;
        if (!TopologyEditOperation.AutoPopulateMeshValues) return;
        if (!topology.Topology.TryGetControlPointContainingRegion(point.Fid, out int regionIndex)) return;
        foreach (ArcWithDirection outerArc in topology.Topology.CompletedRegions[regionIndex].OuterRing.Arcs)
        {
            Arc sourceArc = topology.Topology.Arcs[outerArc.ArcID];
            if (sourceArc.RegionA != -1 && sourceArc.RegionB != -1) continue;
            ObservableConceptualMeshArc arc = topology.Arcs[outerArc.ArcID];
            if (arc.SizeFunction != LinearSizeFunction.Constant) continue;
            SetValidatedProperty(arc, "ConstantSize", point.ConstantSize);
        }
    }

    /// <summary>Mirrors QuadPatchQuickEditor.ResizeNorthSouth/ResizeEastWest.</summary>
    private static void ResizeQuadPatchSide(ObservableConceptualMeshTopology topology,
        ObservableConceptualMeshControlPoint point, string axis, float ratio)
    {
        if (!topology.Topology.TryGetControlPointContainingRegion(point.Fid, out int regionIndex))
            throw new NotSupportedException("The selected control point is not part of a completed region.");
        if (topology.Topology.CompletedRegions[regionIndex].OuterRing.Arcs.Count < 4)
            throw new NotSupportedException("The source quad quick editor requires at least four outer-ring arcs.");

        Topology regionTopology = topology.Topology.CloneRegionAsTopology(regionIndex,
            out _, out _, out Dictionary<int, int> reverseArcIndexMapping, out _);
        regionTopology.Complete();
        var operationResult = new OperationResult();
        MeshRegionMetadata regionData = regionTopology.GetRegionData(0, operationResult);
        if (!operationResult.Result) throw new NotSupportedException("The source region metadata is incomplete for quad quick sizing.");
        regionData = regionTopology.UpdateCorners(0, regionData);
        List<ArcWithDirection> outerArcs = regionTopology.CompletedRegions[0].OuterRing.Arcs;
        List<int> north = CollectOriginalArcIDs(outerArcs, regionData.CornerRingNodeNE, regionData.CornerRingNodeNW, reverseArcIndexMapping);
        List<int> south = CollectOriginalArcIDs(outerArcs, regionData.CornerRingNodeSW, regionData.CornerRingNodeSE, reverseArcIndexMapping);
        List<int> east = CollectOriginalArcIDs(outerArcs, regionData.CornerRingNodeSE, regionData.CornerRingNodeNE, reverseArcIndexMapping);
        List<int> west = CollectOriginalArcIDs(outerArcs, regionData.CornerRingNodeNW, regionData.CornerRingNodeSW, reverseArcIndexMapping);
        double northLength = north.Sum(topology.Topology.GetArcPolylineLength);
        double southLength = south.Sum(topology.Topology.GetArcPolylineLength);
        double eastLength = east.Sum(topology.Topology.GetArcPolylineLength);
        double westLength = west.Sum(topology.Topology.GetArcPolylineLength);
        if ((northLength > eastLength && northLength > westLength) || (southLength > eastLength && southLength > westLength))
        {
            (north, east) = (east, north);
            (south, west) = (west, south);
        }
        IEnumerable<int> selectedArcIDs = axis == "NorthSouth" ? north.Concat(south) : east.Concat(west);
        foreach (int arcID in selectedArcIDs)
        {
            if (arcID < 0 || arcID >= topology.Arcs.Count) throw new NotSupportedException("A quad side refers to a missing topology arc.");
            ObservableConceptualMeshArc arc = topology.Arcs[arcID];
            if (arc.SizeFunction == LinearSizeFunction.Constant)
                SetValidatedProperty(arc, "ConstantSize", arc.ConstantSize * ratio);
            else if (arc.SizeFunction == LinearSizeFunction.CountWithBias)
                SetValidatedProperty(arc, "PointCount", (int)((float)arc.PointCount * ratio));
            else throw new NotSupportedException("The source quad quick editor cannot resize an arc with an Unset size function.");
        }
    }

    private static List<int> CollectOriginalArcIDs(List<ArcWithDirection> localArcs, int startNode, int endNode,
        Dictionary<int, int> reverseArcIndexMapping)
    {
        int count = localArcs.Count;
        int arcCount = endNode > startNode ? endNode - startNode : endNode - startNode + count;
        var result = new List<int>(arcCount);
        for (int i = 0; i < arcCount; i++)
        {
            int localArcID = localArcs[(startNode + i) % count].ArcID;
            result.Add(reverseArcIndexMapping[localArcID]);
        }
        return result;
    }

    private static ObservableConceptualMeshArc FindArc(ObservableConceptualMeshTopology topology, int fid) =>
        fid >= 0 ? topology.Arcs.FirstOrDefault(arc => arc.Fid == fid)
            ?? throw new ArgumentException("The selected arc no longer exists: " + fid.ToString(CultureInfo.InvariantCulture))
            : throw new ArgumentException("The selected arc identity is invalid.");

    private static ObservableConceptualMeshControlPoint FindRegion(ObservableConceptualMeshTopology topology, int fid) =>
        fid >= 0 ? topology.ControlPoints.FirstOrDefault(point => point.Fid == fid)
            ?? throw new ArgumentException("The selected region no longer exists: " + fid.ToString(CultureInfo.InvariantCulture))
            : throw new ArgumentException("The selected region identity is invalid.");

    private static void SetArcField(ObservableConceptualMeshArc arc, string field, string text)
    {
        switch (field)
        {
            case "SizeFunction": SetValidatedProperty(arc, field, ParseEnum<LinearSizeFunction>(text)); break;
            case "PointCount": SetValidatedProperty(arc, field, int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)); break;
            case "Bias": SetValidatedProperty(arc, field, ParseFloat(text)); break;
            case "ConstantSize": SetValidatedProperty(arc, field, ParseFloat(text)); break;
            case "ConstrainStretching": SetValidatedProperty(arc, field, ParseEnum<ConstrainStretchingOptions>(text)); break;
            case "LaplaceBC": SetValidatedProperty(arc, field, ParseEnum<LaplaceBCOptions>(text)); break;
            case "DistanceStraightLine": SetValidatedProperty(arc, field, bool.Parse(text)); break;
            case "AreaWeight": SetValidatedProperty(arc, field, ParseFloat(text)); break;
            case "DiscretizeMagnetism": SetValidatedProperty(arc, field, ParseFloat(text)); break;
            case "FollowExactly": SetValidatedProperty(arc, field, bool.Parse(text)); break;
            default: throw new ArgumentException("Unsupported arc property: " + field);
        }
    }

    private static void SetRegionField(ObservableConceptualMeshControlPoint point, string field, string text)
    {
        switch (field)
        {
            case "PatchType": SetValidatedProperty(point, field, ParseEnum<PatchType>(text)); break;
            case "ConstantSize":
                SetValidatedProperty(point, field, ParseFloat(text));
                PushRegionSizingToOuterArcs(point.Topology, point);
                break;
            case "SecondaryConstantSize": SetValidatedProperty(point, field, ParseFloat(text)); break;
            case "RotationDegrees": SetValidatedProperty(point, field, ParseFloat(text)); break;
            case "BiasInternal": SetValidatedProperty(point, field, ParseFloat(text)); break;
            case "Multipoint": SetValidatedProperty(point, field, bool.Parse(text)); break;
            case "TinResolution": SetValidatedProperty(point, "TinResolution", ParseFloat(text)); break;
            case "SizeTolerance": SetValidatedProperty(point, field, ParseFloat(text)); break;
            case "Recipe": SetValidatedProperty(point, field, text); break;
            default: throw new ArgumentException("Unsupported region property: " + field);
        }
    }

    /// <summary>
    /// Mirrors ConceptualMeshControlPointEditor.ForceReset* for the six
    /// multi-selection clear commands. The source assigns float.NaN directly
    /// through each ObservableConceptualMeshControlPoint setter; it does not
    /// run the ordinary finite-number/DataAnnotations validation path.
    /// </summary>
    private static bool ForceResetRegionField(ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        var (points, property) = RegionResetTargets(topology, mutation);
        float[] current = points.Select(point => (float)(property.GetValue(point)
            ?? throw new InvalidOperationException("The source reset field has no value."))).ToArray();
        bool mismatch = current.Skip(1).Any(value => !value.Equals(current[0]));
        if (!mismatch) return false;

        // Resolve and validate the entire selection before mutating any row.
        foreach (ObservableConceptualMeshControlPoint point in points)
        {
            property.SetValue(point, float.NaN);
            if (property.GetValue(point) is not float updated || !float.IsNaN(updated))
                throw new InvalidOperationException("The source topology did not accept the reset value.");
        }
        return true;
    }

    /// <summary>
    /// Mirrors ConceptualMeshControlPointEditor.ForceSetPatchType. The source
    /// command is intentionally a multi-selection assignment and remains an
    /// undoable topology mutation until the parent model save commits it.
    /// </summary>
    private static (ObservableConceptualMeshControlPoint[] Points, PatchType PatchType) PatchTypeTargets(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.Operation, "forceSetPatchType", StringComparison.Ordinal)
            || !string.Equals(mutation.FeatureKind, "region", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 0
            || mutation.FieldID is not null || mutation.Value is null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length == 0
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null)
            throw new ArgumentException("A patch-type Force Set command has an invalid shape.");

        int[] fids = UniqueFeatureIDs(mutation.ControlPointFeatureIDs, "control point");
        if (!Enum.TryParse(mutation.Value, ignoreCase: false, out PatchType patchType) || !Enum.IsDefined(patchType))
            throw new ArgumentException("The selected patch type is invalid.");
        ObservableConceptualMeshControlPoint[] points = fids.Select(fid => FindRegion(topology, fid)).ToArray();
        return (points, patchType);
    }

    /// <summary>
    /// Mirrors ConceptualMeshControlPointEditor.ForceSetMultipoint. The
    /// command accepts one bool value and assigns it to every exact selected
    /// control-point FID; the resulting mutation remains undoable in the
    /// geometry topology draft.
    /// </summary>
    private static (ObservableConceptualMeshControlPoint[] Points, bool Value) RegionMultipointTargets(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.Operation, "forceSetRegionMultipoint", StringComparison.Ordinal)
            || !string.Equals(mutation.FeatureKind, "region", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 0
            || mutation.FieldID is not null || mutation.Value is null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length == 0
            || mutation.NodeFeatureIDs is null || mutation.NodeFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null
            || mutation.ImportPayload is not null
            || !bool.TryParse(mutation.Value, out bool value))
            throw new ArgumentException("A Multipoint Force Set command has an invalid shape or value.");

        int[] fids = UniqueFeatureIDs(mutation.ControlPointFeatureIDs, "control point");
        ObservableConceptualMeshControlPoint[] points = fids.Select(fid => FindRegion(topology, fid)).ToArray();
        return (points, value);
    }

    /// <summary>
    /// Mirrors ConceptualMeshArcEditor.ForceSetCommand for Constrain Stretching
    /// and Laplace BC. It deliberately assigns the chosen value to every exact
    /// selected source arc, including arcs already equal to the displayed
    /// aggregate value. The edit remains in the parent geometry draft and is
    /// undoable; only the parent model save writes the HDF.
    /// </summary>
    private static (ObservableConceptualMeshArc[] Arcs, string FieldID, string Value) ArcChoiceTargets(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.Operation, "forceSetArcChoice", StringComparison.Ordinal)
            || !string.Equals(mutation.FeatureKind, "arc", StringComparison.Ordinal)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length == 0
            || mutation.FieldID is not ("ConstrainStretching" or "LaplaceBC") || mutation.Value is null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.NodeFeatureIDs is null || mutation.NodeFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null
            || mutation.ImportPayload is not null)
            throw new ArgumentException("An Arc choice Force Set command has an invalid shape.");

        string[] allowedValues = mutation.FieldID == "ConstrainStretching"
            ? ["Default", "Constrained", "Floating"]
            : ["Default", "ZeroFlux", "Dirichlet"];
        if (!allowedValues.Contains(mutation.Value, StringComparer.Ordinal))
            throw new ArgumentException("The selected Arc choice is invalid.");

        int[] fids = UniqueFeatureIDs(mutation.FeatureIDs, "arc");
        ObservableConceptualMeshArc[] arcs = fids.Select(fid => FindArc(topology, fid)).ToArray();
        return (arcs, mutation.FieldID, mutation.Value);
    }

    private static string ArcChoiceValue(ObservableConceptualMeshArc arc, string fieldID) => fieldID switch
    {
        "ConstrainStretching" => arc.ConstrainStretching.ToString(),
        "LaplaceBC" => arc.LaplaceBC.ToString(),
        _ => throw new ArgumentException("Unsupported Arc choice field.")
    };

    /// <summary>
    /// Mirrors ConceptualMeshArcEditor.ForceResetBias: Clear assigns NaN to
    /// every selected arc. This deliberately bypasses the ordinary finite
    /// numeric property validation and remains an undoable draft edit.
    /// </summary>
    private static ObservableConceptualMeshArc[] ArcBiasResetTargets(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        if (!string.Equals(mutation.Operation, "forceResetArcBias", StringComparison.Ordinal)
            || !string.Equals(mutation.FeatureKind, "arc", StringComparison.Ordinal)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length == 0
            || !string.Equals(mutation.FieldID, "Bias", StringComparison.Ordinal) || mutation.Value is not null
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.NodeFeatureIDs is null || mutation.NodeFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null
            || mutation.ImportPayload is not null)
            throw new ArgumentException("An Arc Bias reset command has an invalid shape.");

        int[] fids = UniqueFeatureIDs(mutation.FeatureIDs, "arc");
        return fids.Select(fid => FindArc(topology, fid)).ToArray();
    }

    private static (ObservableConceptualMeshControlPoint[] Points, PropertyInfo Property) RegionResetTargets(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        string[] resettableFields = ["ConstantSize", "SecondaryConstantSize", "RotationDegrees",
            "BiasInternal", "TinResolution", "SizeTolerance"];
        if (!string.Equals(mutation.FeatureKind, "region", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs.Length < 2 || mutation.FieldID is null
            || !resettableFields.Contains(mutation.FieldID, StringComparer.Ordinal)
            || mutation.Value is not null || mutation.TagName is not null || mutation.TagType is not null
            || mutation.TagValue is not null || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs.Length != 0 || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet || mutation.PointKind is not null
            || mutation.InternalPointIndex is not null || mutation.EndpointNodeIndex is not null
            || mutation.Axis is not null || mutation.RawValue is not null)
            throw new ArgumentException("A source region reset command has an invalid shape.");

        int[] fids = UniqueFeatureIDs(mutation.FeatureIDs, "control point");
        ObservableConceptualMeshControlPoint[] points = fids.Select(fid => FindRegion(topology, fid)).ToArray();
        PropertyInfo property = typeof(ObservableConceptualMeshControlPoint).GetProperty(mutation.FieldID)
            ?? throw new ArgumentException("Unsupported source region reset property.");
        if (property.PropertyType != typeof(float) || property.SetMethod is null)
            throw new ArgumentException("The source region reset field is not a writable single-precision value.");
        return (points, property);
    }

    /// <summary>
    /// Mirrors the six ConceptualMeshControlPointEditor string setters. Those
    /// setters parse with the current UI culture, map an unparseable/empty
    /// value to NaN, and assign the value to every selected point without
    /// passing through the stricter table-cell validator.
    /// </summary>
    private static (ObservableConceptualMeshControlPoint[] Points, PropertyInfo Property) RegionEditorInputTargets(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        string[] editableFields = ["ConstantSize", "SecondaryConstantSize", "RotationDegrees",
            "BiasInternal", "TinResolution", "SizeTolerance"];
        if (!string.Equals(mutation.Operation, "setRegionInput", StringComparison.Ordinal)
            || !string.Equals(mutation.FeatureKind, "region", StringComparison.OrdinalIgnoreCase)
            || mutation.FeatureIDs is null || mutation.FeatureIDs.Length == 0 || mutation.FieldID is null
            || !editableFields.Contains(mutation.FieldID, StringComparer.Ordinal)
            || mutation.Value is null || mutation.TagName is not null || mutation.TagType is not null
            || mutation.TagValue is not null || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs.Length != 0 || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet || mutation.PointKind is not null
            || mutation.InternalPointIndex is not null || mutation.EndpointNodeIndex is not null
            || mutation.Axis is not null || mutation.RawValue is not null)
            throw new ArgumentException("A source Conceptual Mesh input command has an invalid shape.");

        int[] fids = UniqueFeatureIDs(mutation.FeatureIDs, "control point");
        ObservableConceptualMeshControlPoint[] points = fids.Select(fid => FindRegion(topology, fid)).ToArray();
        PropertyInfo property = typeof(ObservableConceptualMeshControlPoint).GetProperty(mutation.FieldID)
            ?? throw new ArgumentException("Unsupported source Conceptual Mesh input property.");
        if (property.PropertyType != typeof(float) || property.SetMethod is null)
            throw new ArgumentException("The source Conceptual Mesh input is not a writable single-precision value.");
        return (points, property);
    }

    private static string CommonRegionEditorText(ObservableConceptualMeshControlPoint[] points, PropertyInfo property)
    {
        float first = (float)(property.GetValue(points[0])
            ?? throw new InvalidOperationException("The source region input has no value."));
        if (points.Skip(1).Any(point => property.GetValue(point) is not float value || !object.Equals(value, first)))
            return string.Empty;
        return object.Equals(first, float.NaN) ? string.Empty : first.ToString();
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new ArgumentException("Invalid " + typeof(T).Name + " value.");

    private static float ParseFloat(string value)
    {
        float parsed = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!float.IsFinite(parsed)) throw new ArgumentException("Numeric values must be finite.");
        return parsed;
    }

    private static void SetValidatedProperty(object target, string propertyName, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName)
            ?? throw new ArgumentException("Unsupported source property: " + propertyName);
        var validationContext = new ValidationContext(target) { MemberName = propertyName };
        foreach (ValidationAttribute attribute in property.GetCustomAttributes<ValidationAttribute>())
        {
            ValidationResult? result = attribute.GetValidationResult(value, validationContext);
            if (result is null || !BlocksValidatedProperty(result)) continue;
            throw new ValidationException(result, attribute, value);
        }
        property.SetValue(target, value);
        if (!Equals(property.GetValue(target), value)) throw new ValidationException("The source topology did not accept " + propertyName + ".");
    }

    private static bool BlocksValidatedProperty(ValidationResult result) =>
        result is not IErrorLevel leveled
        || leveled.ErrorLevel.AsSeverity() == MessageSeverity.Error;

    private static void CreateTag(ObservableConceptualMeshTopology topology, string kind, string? name, string? tagTypeText)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tagTypeText)) throw new ArgumentException("A tag name and type are required.");
        if (!Enum.TryParse(tagTypeText, ignoreCase: false, out TagType tagType) || !Enum.IsDefined(tagType)) throw new ArgumentException("Invalid tag type.");
        _ = TagTable(topology, kind);
        if (topology.IsReservedColumn(name)) throw new ArgumentException("The selected name is reserved by the source topology.");
        if (kind == "arc") topology.CreateNewArcTag(name, tagType);
        else if (kind == "region") topology.CreateNewControlPointTag(name, tagType);
        else throw new ArgumentException("Tags can only be attached to arcs or regions.");
    }

    private static GeometryTopologyDraftState RememberRecentTag(GeometryTopologyDraftState state,
        GeometryTopologyMutation mutation, ObservableConceptualMeshTopology topology)
    {
        if (mutation.Operation is not ("setTag" or "removeTag") || string.IsNullOrWhiteSpace(mutation.TagName)) return state;
        string kind = mutation.FeatureKind.ToLowerInvariant();
        ObservableTable table = TagTable(topology, kind);
        if (!table.TryGetColumnIndex(mutation.TagName, out int columnIndex)) return state;
        Type valueType = table.Columns[columnIndex].Type;
        TagType tagType = TagTypeHelper.AsTagType(valueType);
        object value = mutation.Operation == "removeTag"
            ? DefaultTagValue(valueType)
            : ParseTagValue(mutation.TagValue ?? throw new ArgumentException("A tag value is required."), valueType);
        var recent = new GeometryTopologyRecentTag(mutation.TagName, tagType.ToString(), Format(value));
        GeometryTopologyRecentTag[] current = kind == "arc"
            ? state.RecentArcTags : kind == "region"
                ? state.RecentRegionTags : [];
        GeometryTopologyRecentTag[] updated = [recent, .. current.Where(item =>
            !string.Equals(item.Name, recent.Name, StringComparison.Ordinal) || !string.Equals(item.Value, recent.Value, StringComparison.Ordinal))];
        return kind == "arc"
            ? state with { RecentArcTags = updated }
            : kind == "region"
                ? state with { RecentRegionTags = updated }
                : state;
    }

    private static void SetTagValues(ObservableConceptualMeshTopology topology, string kind, int[] featureIDs, string? name, string? text)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A tag name is required.");
        if (featureIDs.Length == 0) throw new ArgumentException("Select at least one source feature.");
        ObservableTable table = TagTable(topology, kind);
        if (!table.TryGetColumnIndex(name, out int columnIndex)) throw new ArgumentException("Create the tag before assigning values.");
        TableColumn column = table.Columns[columnIndex];
        if (column.IsReadOnly) throw new ArgumentException("The selected source tag is read-only.");
        object value = text is null ? DefaultTagValue(column.Type) : ParseTagValue(text, column.Type);
        int featureCount = kind switch { "arc" => topology.Arcs.Count, "region" => topology.ControlPoints.Count, _ => throw new ArgumentException("Tags can only be attached to arcs or regions.") };
        foreach (int fid in featureIDs.Distinct())
        {
            if (fid < 0 || fid >= featureCount) throw new ArgumentException("A selected feature identity no longer exists.");
            table.SetCellValue(fid, name, value);
        }
    }

    private static ObservableTable TagTable(ObservableConceptualMeshTopology topology, string kind) => kind switch
    {
        "arc" => topology.ArcMetadata,
        "region" => topology.ControlPointMetadata,
        _ => throw new ArgumentException("Tags can only be attached to arcs or regions.")
    };

    private static object DefaultTagValue(Type type) => type == typeof(string) ? ""
        : type == typeof(int) ? 0 : type == typeof(float) ? 0f : type == typeof(double) ? 0d
        : type == typeof(bool) ? false : throw new ArgumentException("Unsupported tag column type.");

    private static object ParseTagValue(string value, Type type)
    {
        if (type == typeof(string)) return value;
        if (type == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (type == typeof(float)) return ParseFloat(value);
        if (type == typeof(double))
        {
            double parsed = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!double.IsFinite(parsed)) throw new ArgumentException("Numeric values must be finite.");
            return parsed;
        }
        if (type == typeof(bool)) return bool.Parse(value);
        throw new ArgumentException("Unsupported tag column type.");
    }

    private static void VerifyTopologyRoundTrip(GeometryTopologySnapshot expected, GeometryTopologySnapshot actual)
    {
        static bool Same(GeometryTopologyFeatureTable left, GeometryTopologyFeatureTable right) =>
            left.Rows.Length == right.Rows.Length && left.Rows.OrderBy(row => row.FID)
                .Zip(right.Rows.OrderBy(row => row.FID), (a, b) => a.FID == b.FID && DictionaryEqual(a.Values, b.Values)).All(value => value);
        if (!Same(expected.Arcs, actual.Arcs) || !Same(expected.Regions, actual.Regions) || !Same(expected.Nodes, actual.Nodes))
            throw new ValidationException("The staged topology did not match the requested source edits.");
    }

    private static bool DictionaryEqual(Dictionary<string, string> left, Dictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out string? value) && value == pair.Value);
}

/// <summary>Rejects rendering calls while allowing retained topology edit callbacks to stay headless.</summary>
public class GeometryTopologyHeadlessVectorManagerProxy : DispatchProxy
{
    public GeometryTopologyHeadlessVectorManagerProxy() { }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        string method = targetMethod?.Name ?? "unknown vector-manager method";
        throw new NotSupportedException("Retained topology completion attempted unsupported headless vector-manager call " + method + ".");
    }
}
