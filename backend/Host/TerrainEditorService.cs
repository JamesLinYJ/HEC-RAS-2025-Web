using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Rasters.Modifiers;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Observables;

namespace Ras.Backend;

public sealed record TerrainInputExtent(double MinX, double MaxX, double MinY, double MaxY);
public sealed record TerrainMapCoordinate(double X, double Y);
public sealed record TerrainMapFeature(string Id, string TableId, TerrainMapCoordinate[] Points, string Kind = "line");
public sealed record TerrainLeveePropertyForceSet(string Command, string Value, string[] RowIDs);
public sealed record TerrainInputData(string Id, string Scope, string Name, string Type, string? Filename, string? Schema, string? Error,
    TerrainInputExtent? ValidExtent = null, int? OverviewCount = null, string[]? Overviews = null,
    int? SourceVectorIndex = null);
public sealed record TerrainTableColumnData(string Id, string Title, string SourceType, bool ReadOnly = false,
    double? Width = null, string[]? AutoSuggestions = null);
public sealed record TerrainTableData(TerrainTableColumnData[] Columns, bool CanAddRows = false, bool CanCopyRows = false,
    bool CanDeleteRows = false, string[]? SelectedRowIDs = null, string[]? RequestedSelectionRowIDs = null);
public sealed record TerrainEditorData(
    TerrainInputData[] Inputs,
    string? SelectedId,
    string? ImportDraftToken = null,
    string? ImportCleanupWarning = null,
    string? ImportCleanupToken = null,
    Dictionary<string, ModelRow[]>? Rows = null,
    Dictionary<string, TerrainTableData>? Tables = null,
    Dictionary<string, SourceEditorChoice[]>? FieldChoices = null,
    Dictionary<string, string[]>? TextSuggestions = null);
public sealed record TerrainTransformRequest(ModelSaveRequest Model, string Operation, string? ItemId = null, int? From = null, int? To = null,
    string? InputKind = null, TerrainInputExtent? HintExtent = null, string? TableId = null, string[]? RowIDs = null,
    TerrainLeveePropertyForceSet? LeveeForceSet = null);
public sealed record TerrainMapRequest(ModelSaveRequest Model, string? InputId = null);
public sealed record TerrainMapData(SpatialGridPreview? Raster, SpatialGridPreview? SelectedRaster = null,
    string? InputId = null, string? SourceRevision = null, TerrainMapFeature[]? Features = null);

public static partial class ModelEditorAdapter
{
    private static TerrainEditor? TerrainInputs(ModelContext context, string asset, string revision, string? state) =>
        context.Layer is Terrain layer && context.Observable is ObservableTerrain observable
            ? new(layer, asset, revision, state, observable) : null;
    private static void CheckTerrain(ModelSaveRequest model)
    {
        VerifyKind(model);
        if (model.ProjectPath == null) throw new ArgumentException("Terrain edits require the owning project.");
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
    }
    public static async Task<ModelSnapshot> TransformTerrainAsync(TerrainTransformRequest request)
    {
        CheckTerrain(request.Model);
        using var context = new ModelContext(request.Model);
        var editor = TerrainInputs(context, request.Model.AssetPath, request.Model.Revision, request.Model.SourceState) ?? throw new ArgumentException("This model is not a terrain.");
        editor.CaptureSelectedInput(request.Model.Fields);
        editor.ApplySelections(request.Model.TableSelections);
        ApplyFields(context, null, request.Model.Fields); editor.Apply(request.Model.Fields);
        editor.ApplyRows(request.Model.Rows, request.Model.ChangedCells);
        editor.Transform(request);
        var result = await Snapshot(context, request.Model.Revision, true, null, terrain: editor);
        CheckTerrain(request.Model); return result;
    }
    public static Task<TerrainMapData> TerrainMapAsync(TerrainMapRequest request)
    {
        CheckTerrain(request.Model);
        using var context = new ModelContext(request.Model);
        var editor = TerrainInputs(context, request.Model.AssetPath, request.Model.Revision, request.Model.SourceState) ?? throw new ArgumentException("This model is not a terrain.");
        editor.CaptureSelectedInput(request.Model.Fields);
        editor.ApplySelections(request.Model.TableSelections);
        ApplyFields(context, null, request.Model.Fields); editor.Apply(request.Model.Fields); editor.ApplyRows(request.Model.Rows, request.Model.ChangedCells); editor.VerifyDependencies();
        var result = editor.Preview(request.InputId);
        editor.VerifyDependencies(); CheckTerrain(request.Model); return Task.FromResult(result);
    }
    public static Task<TerrainInputImportInspectData> InspectTerrainInputImportAsync(TerrainInputImportInspectRequest request)
    {
        CheckTerrain(request.Model);
        using var context = new ModelContext(request.Model);
        if (context.Layer is not Terrain) throw new ArgumentException("This model is not a terrain.");
        TerrainInputImportInspectData result = TerrainInputImportService.Inspect(request.FilePaths, context.Project.Projection);
        CheckTerrain(request.Model);
        return Task.FromResult(result);
    }
    public static async Task<TerrainInputAppendResult> AppendTerrainInputsAsync(TerrainInputAppendRequest request)
    {
        CheckTerrain(request.Model);
        TerrainInputAppendResult? cachedResult = null;
        TerrainInputAppendResult? committedResult = null;
        using (var context = new ModelContext(request.Model))
        {
            var editor = TerrainInputs(context, request.Model.AssetPath, request.Model.Revision, request.Model.SourceState)
                ?? throw new ArgumentException("This model is not a terrain.");
            editor.CaptureSelectedInput(request.Model.Fields);
            editor.ApplySelections(request.Model.TableSelections);
            ApplyFields(context, null, request.Model.Fields);
            editor.Apply(request.Model.Fields);
            editor.ApplyRows(request.Model.Rows, request.Model.ChangedCells);
            TerrainImportBatchJournal journal = await editor.AppendAsync(request, context.Project.Projection);
            if (journal.Result is { } existingResult) cachedResult = existingResult;
            else
            {
                editor.VerifyDependencies();
                ModelSnapshot result = await Snapshot(context, request.Model.Revision, true, null, terrain: editor);
                CheckTerrain(request.Model);
                editor.VerifyDependencies();
                TerrainInputAppendResult envelope = new(result, journal.DraftToken, journal.BatchToken, journal.Mode,
                    journal.State, journal.Operations.Select(operation => operation.ToResult()).OrderBy(operation => operation.Order).ToArray());
                committedResult = TerrainInputImportService.CommitResult(journal, envelope);
            }
        }

        if (cachedResult is not null)
        {
            VerifyCachedImportResult(request, cachedResult);
            return cachedResult;
        }
        return committedResult ?? throw new InvalidOperationException("The terrain import did not produce a result envelope.");
    }
    public static async Task<TerrainInputImportStatusData> GetTerrainInputImportStatusAsync(TerrainInputImportStatusRequest request)
    {
        TerrainInputAppendRequest appendRequest = request.Request;
        VerifyKind(appendRequest.Model);
        if (appendRequest.Model.Kind != "Terrain" || string.IsNullOrWhiteSpace(appendRequest.Model.ProjectPath))
            throw new ArgumentException("Terrain import status requires the owning terrain project.");
        TerrainInputImportStatusData status = TerrainInputImportService.ReadStatus(appendRequest);
        if (status.State is "NotFound" or "Running") return status;
        if (status.Result is { } cachedResult)
        {
            try { VerifyCachedImportResult(appendRequest, cachedResult); }
            catch (SourceFileChangedException error)
            {
                return new(status.State, null, status.Operations, "sourceChanged", error.Message);
            }
            return status;
        }

        // The conversion journal may be terminal before its snapshot envelope
        // is committed. Reuse the same digest/token path to attach staged
        // outputs and make status recovery return the exact final result.
        CheckTerrain(appendRequest.Model);
        TerrainInputAppendResult result = await AppendTerrainInputsAsync(appendRequest);
        return new(result.BatchStatus, result, result.Operations);
    }

    private static void VerifyCachedImportResult(TerrainInputAppendRequest request, TerrainInputAppendResult result)
    {
        CheckTerrain(request.Model);
        if (result.Snapshot.Revision != request.Model.Revision
            || result.Snapshot.Terrain is not { } terrain
            || terrain.ImportDraftToken != result.DraftToken
            || string.IsNullOrWhiteSpace(result.Snapshot.SourceState))
            throw new SourceFileChangedException();

        // The cached envelope is installable only while its actual HDF source
        // dependencies and each successfully staged TIFF still match the
        // captured draft state. Imported request files that were merely read
        // during conversion are intentionally not promoted to draft
        // dependencies.
        using var context = new ModelContext(request.Model);
        TerrainEditor editor = TerrainInputs(context, request.Model.AssetPath,
            request.Model.Revision, result.Snapshot.SourceState)
            ?? throw new ArgumentException("This model is not a terrain.");
        editor.VerifyDependencies();
        CheckTerrain(request.Model);
    }

    public static TerrainInputImportCancelResult CancelTerrainInputImport(TerrainInputImportCancelRequest request)
    {
        VerifyKind(request.Model);
        if (request.Model.Kind != "Terrain" || string.IsNullOrWhiteSpace(request.Model.ProjectPath))
            throw new ArgumentException("Terrain import cancellation requires the owning terrain project.");
        if (!File.Exists(request.Model.AssetPath)) throw new FileNotFoundException("The terrain model file was not found.", request.Model.AssetPath);
        return TerrainInputImportService.RequestCancelAsync(request).GetAwaiter().GetResult();
    }
    public static bool DiscardTerrainInputImport(TerrainInputImportDiscardRequest request)
    {
        if (request.Model.Kind != "Terrain" || string.IsNullOrWhiteSpace(request.Model.ProjectPath))
            throw new ArgumentException("Discarding terrain import drafts requires the owning project.");
        return TerrainInputImportService.Discard(request.Model.AssetPath, request.DraftToken);
    }
    public static bool DiscardTerrainInputImportBatch(TerrainInputImportDiscardBatchRequest request)
    {
        if (request.Model.Kind != "Terrain" || string.IsNullOrWhiteSpace(request.Model.ProjectPath))
            throw new ArgumentException("Discarding terrain import batches requires the owning project.");
        return TerrainInputImportService.DiscardBatch(request.Model.AssetPath, request.DraftToken, request.BatchToken);
    }
    private sealed class TerrainEditor
    {
        internal const string SourceField = "TerrainLayersEditor/SelectedInputWithMap";
        private const string PolygonFeatureTable = "PolygonRasterModificationLayerProperties/Features";
        private const string ChannelFeatureTable = "ChannelModEditor/Features";
        private const string ChannelProfileTable = "ChannelModEditor/Profile";
        private const string ChannelTerrainProfileTable = "ChannelModEditor/TerrainProfile";
        private const string LeveeFeatureTable = "LeveeModsTable";
        private const string LeveeProfileTable = "LeveeModDetailEditor/Profile";
        private const string LeveePropertyTable = "LeveeModPropertiesEditor/PropertySets";
        private const string ChannelTinLengthField = "ChannelModParentEditor/Context_Context_Context_TinMaxSegmentLength";
        private const string ChannelModificationTypeField = "ChannelModParentEditor/Context_Context_Context_ModificationType";
        private const string ChannelNameField = "ChannelModEditor/PrimarySelection_Name";
        private const string ChannelLineLengthField = "ChannelModEditor/SelectedLineLength";
        private const string LeveeNameField = "LeveeModDetailEditor/Levee_Name";
        private const string LeveePropertyNameField = "LeveeModDetailEditor/Levee_LeveePropertyName";
        private const string LeveeLineLengthField = "LeveeModDetailEditor/LineLength";
        private const string LeveePropertyNameFieldSet = "LeveeModPropertiesDetailEditor/Name";
        private const string LeveeModeField = "LeveeModPropertiesDetailEditor/Mode";
        private const string LeveeTopWidthField = "LeveeModPropertiesDetailEditor/TopWidthString";
        private const string LeveeLeftWidthField = "LeveeModPropertiesDetailEditor/LeftWidthString";
        private const string LeveeRightWidthField = "LeveeModPropertiesDetailEditor/RightWidthString";
        private const string LeveeLeftGradeField = "LeveeModPropertiesDetailEditor/LeftGradeString";
        private const string LeveeRightGradeField = "LeveeModPropertiesDetailEditor/RightGradeString";
        private const string LeveeModificationTypeField = "LeveeModPropertiesDetailEditor/ModType";
        private sealed record DraftState(
            string Kind,
            string Asset,
            string Revision,
            string[] Order,
            Dictionary<string, string> Dependencies,
            string? ImportDraftToken = null,
            TerrainPendingImport[]? PendingImports = null,
            TerrainPendingInput[]? PendingInputs = null,
            Dictionary<string, ChannelDraftData>? ChannelDrafts = null,
            Dictionary<string, LeveeDraftData>? LeveeDrafts = null,
            Dictionary<string, TerrainTopologyDraft>? TopologyDrafts = null,
            Dictionary<string, TerrainPolygonTopologyDraft>? PolygonTopologyDrafts = null,
            Dictionary<string, TerrainPolygonEditSettings>? PolygonSettings = null);
        private sealed record TerrainPendingInput(string Id, string Kind, string Name, TerrainInputExtent? HintExtent = null);
        private sealed record DraftPoint(double X, double Y);
        private sealed record DraftLine(DraftPoint[] Points, bool HasZ, bool HasM, string[]? Z, string[]? M);
        private sealed record DraftProfile(string[] Stations, string[] Elevations);
        private sealed record ChannelFeatureDraft(string? Name, DraftLine? Line, DraftProfile Profile);
        private sealed record ChannelDraftData(string TinMaxSegmentLength, string ModificationType, ChannelFeatureDraft[] Features,
            TerrainChannelGeometryPayload[]? ImportedGeometryPayloads = null);
        private sealed record LeveeFeatureDraft(string? Name, string? PropertyName, DraftLine? Line, DraftProfile Profile);
        private sealed record LeveePropertyDraft(string? Name, string Mode, string TopWidth, string LeftWidth,
            string RightWidth, string LeftGrade, string RightGrade, string ModificationType);
        private sealed record LeveeDraftData(LeveeFeatureDraft[] Features, LeveePropertyDraft[] Properties);
        private readonly Terrain layer;
        private readonly ObservableTerrain observableTerrain;
        private readonly string asset, revision;
        private readonly Dictionary<string, ITerrainInput> inputs;
        private readonly Dictionary<string, int> sourceVectorIndices = new(StringComparer.Ordinal);
        // Source tree identity is captured before parent draft fields can rename an input.
        // DraftState.Revision pins the journal to the exact saved terrain source SHA.
        private readonly Dictionary<string, string> sourcePolygonNodeIDs = new(StringComparer.Ordinal);
        private Dictionary<string, string> dependencies = new(StringComparer.Ordinal);
        private readonly List<TerrainPendingImport> pendingImports = [];
        private readonly List<TerrainPendingInput> pendingInputs = [];
        private readonly Dictionary<string, Geospatial.Vectors.Topology.Topology> topologyBaselines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TerrainTopologyDraft> topologyDrafts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PolygonRasterMod[]> polygonBaselines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TerrainPolygonTopologyDraft> polygonTopologyDrafts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TerrainPolygonEditSettings> polygonSettings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, TerrainChannelGeometryPayload>> channelGeometryPayloads = new(StringComparer.Ordinal);
        // Row selection is navigation state. It deliberately remains outside
        // DraftState so changing the inspector selection never changes the
        // owning terrain model's opaque draft identity.
        private readonly Dictionary<string, string[]> selectedRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> requestedSelections = new(StringComparer.Ordinal);
        // Capture table row identities before any field mutation in the same
        // request. Some source rows are content-addressed, so changing their
        // selected detail fields can legitimately change the IDs returned by
        // Rows() before the accompanying cell edits are applied.
        private Dictionary<string, ModelRow[]> capturedTableRows = new(StringComparer.Ordinal);
        // A selected property set can be edited (and therefore get a new
        // content-derived row ID) during this request. Keep the exact objects
        // captured by ApplySelections so the edit cannot drift to another row.
        private LeveeModProperties[] selectedPropertyObjects = [];
        // Selected-property row IDs include mutable property values. Keep the
        // request's original IDs bound to the actual objects before applying
        // any fields, because editing Name/Mode/width changes the visible ID.
        private readonly Dictionary<string, (LeveeModTerrainInput Input, LeveeModProperties Property)> capturedPropertyRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Geospatial.PairedData.StationElevationProfile Profile, int Index)> capturedProfileRows = new(StringComparer.Ordinal);
        private string? importDraftToken;
        private string? selected;
        public int? SelectedIndex => selected == null ? null : Array.IndexOf(OrderedIDs(), selected);
        public string? ImportDraftToken => importDraftToken;
        public void CaptureSelectedInput(Dictionary<string, string> fields)
        {
            if (!fields.TryGetValue(SourceField, out string? id)) return;
            if (id.Length > 0 && !OrderedIDs().Contains(id)) throw new ArgumentException("The selected terrain input no longer exists.");
            string? nextSelected = id.Length == 0 ? null : id;
            if (!string.Equals(selected, nextSelected, StringComparison.Ordinal))
            {
                selectedRows.Clear();
                selectedPropertyObjects = [];
                capturedPropertyRows.Clear();
                capturedProfileRows.Clear();
            }
            selected = nextSelected;
        }
        public TerrainEditor(Terrain layer, string asset, string revision, string? state,
            ObservableTerrain observableTerrain)
        {
            this.layer = layer; this.asset = Path.GetFullPath(asset); this.revision = revision;
            this.observableTerrain = observableTerrain;
            var originalInputs = layer.Layers.Select((input, i) => (input,
                id: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.asset + "\n" + revision + "\nterrain:" + i))))).ToArray();
            inputs = originalInputs.ToDictionary(x => x.id, x => x.input, StringComparer.Ordinal);
            int sourceVectorIndex = 0;
            foreach (var item in originalInputs.Where(item => IsSourceVectorInput(item.input)))
            {
                int savedVectorIndex = sourceVectorIndex++;
                sourceVectorIndices.Add(item.id, savedVectorIndex);
                if (item.input is PolygonRasterModTerrainInput)
                    sourcePolygonNodeIDs.Add(item.id, CatalogTreeService.TerrainInputNodeID(this.asset,
                        "PolygonRasterModTerrainInputVisualContext", item.input.Name, savedVectorIndex));
            }
            dependencies = ReadDependencies();
            Dictionary<string, TerrainTopologyDraft> restoredTopologyDrafts = new(StringComparer.Ordinal);
            Dictionary<string, TerrainPolygonTopologyDraft> restoredPolygonTopologyDrafts = new(StringComparer.Ordinal);
            Dictionary<string, TerrainPolygonEditSettings> restoredPolygonSettings = new(StringComparer.Ordinal);
            if (state != null)
            {
                var draft = JsonSerializer.Deserialize<DraftState>(state, BackendJson.Options) ?? throw new ArgumentException("Missing terrain draft state.");
                if (draft.Kind != "Terrain" || draft.Asset != this.asset || draft.Revision != revision) throw new ArgumentException("The terrain draft belongs to another model revision.");
                importDraftToken = draft.ImportDraftToken == null ? null : TerrainInputImportService.NormalizeToken(draft.ImportDraftToken, "terrain import draft");
                TerrainPendingImport[] pending = draft.PendingImports ?? [];
                if (pending.Any(item => item is null))
                    throw new ArgumentException("The terrain import draft contains an empty pending raster.");
                if (pending.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != pending.Length
                    || pending.Select(item => item.FileName).Distinct(StringComparer.Ordinal).Count() != pending.Length)
                    throw new ArgumentException("The terrain import draft contains duplicate identities.");
                if (pending.Length > 0 && importDraftToken == null)
                    throw new ArgumentException("The terrain import draft is missing its cleanup token.");
                foreach (TerrainPendingImport item in pending)
                {
                    ValidatePending(item);
                    string pendingPath = PendingPath(item);
                    if (!File.Exists(pendingPath) || TerrainInputImportService.HashFileContents(pendingPath) != item.Revision)
                        throw new SourceFileChangedException();
                    RasterTerrainInput input = RasterTerrainInput.CreateAndScan(layer, pendingPath);
                    input.Name = item.Name;
                    layer.Add(input);
                    inputs.Add(item.Id, input);
                    pendingImports.Add(item);
                }
                TerrainPendingInput[] created = draft.PendingInputs ?? [];
                if (created.Any(item => item is null)
                    || created.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != created.Length
                    || created.Any(item => string.IsNullOrWhiteSpace(item.Id) || inputs.ContainsKey(item.Id)))
                    throw new ArgumentException("The terrain draft contains an invalid created input.");
                foreach (TerrainPendingInput item in created)
                {
                    if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(H5Reader.SanitizeObjectName(item.Name)))
                        throw new ArgumentException("The terrain draft contains an invalid created input name.");
                    ITerrainInput input = CreateInput(item.Kind, item.Name, item.HintExtent);
                    layer.Add(input);
                    inputs.Add(item.Id, input);
                    pendingInputs.Add(item);
                }
                Dictionary<string, string> currentDependencies = ReadDependencies();
                if (draft.Dependencies == null || !SameDependencies(draft.Dependencies, currentDependencies)) throw new SourceFileChangedException();
                dependencies = currentDependencies;
                if (draft.Order.Distinct(StringComparer.Ordinal).Count() != draft.Order.Length || draft.Order.Any(id => !inputs.ContainsKey(id))) throw new ArgumentException("Invalid terrain input identities.");
                foreach (string id in OrderedIDs().Where(id => !draft.Order.Contains(id)).ToArray())
                {
                    ITerrainInput removed = inputs[id];
                    layer.Remove(removed);
                    inputs.Remove(id);
                    if (removed is IDisposable disposable) disposable.Dispose();
                }
                for (int i = 0; i < draft.Order.Length; i++) layer.Reorder(Array.IndexOf(OrderedIDs(), draft.Order[i]), i);
                RestoreStructureDrafts(draft);
                restoredTopologyDrafts = draft.TopologyDrafts ?? new(StringComparer.Ordinal);
                restoredPolygonTopologyDrafts = draft.PolygonTopologyDrafts ?? new(StringComparer.Ordinal);
                restoredPolygonSettings = draft.PolygonSettings ?? new(StringComparer.Ordinal);
            }
            foreach (var pair in inputs.Where(pair => pair.Value is ChannelModTerrainInput))
            {
                var channel = (ChannelModTerrainInput)pair.Value;
                topologyBaselines.Add(pair.Key, channel.Topology.Clone());
            }
            foreach (var pair in inputs.Where(pair => pair.Value is PolygonRasterModTerrainInput && sourceVectorIndices.ContainsKey(pair.Key)))
                polygonBaselines.Add(pair.Key, CapturePolygonBaseline((PolygonRasterModTerrainInput)pair.Value));
            foreach (var pair in restoredPolygonSettings)
            {
                if (!inputs.TryGetValue(pair.Key, out ITerrainInput? input) || input is not PolygonRasterModTerrainInput)
                    throw new ArgumentException("The terrain polygon settings do not match a source polygon input.");
                ValidatePolygonSettings(pair.Value);
                polygonSettings.Add(pair.Key, pair.Value);
            }
            RestoreTopologyDrafts(restoredTopologyDrafts);
            RestorePolygonTopologyDrafts(restoredPolygonTopologyDrafts);
        }
        public static bool ContainsField(string field) => field == SourceField
            || field.StartsWith("TerrainInputs/", StringComparison.Ordinal)
            || field.StartsWith("TerrainSelection/", StringComparison.Ordinal)
            || field.StartsWith("TerrainModCreator/", StringComparison.Ordinal);
        private string[] OrderedIDs() => layer.Layers.Select(input => inputs.Single(pair => ReferenceEquals(pair.Value, input)).Key).ToArray();
        private static string Scope(string id) => "TerrainInputs/" + id;
        private static string? Schema(ITerrainInput input) => input switch
        {
            RasterTerrainInput => "RasterTerrainInputProperties",
            FunctionalTerrainInput => "FunctionalTerrainInputProperties",
            ConstantTerrainInput => "ConstantTerrainInput",
            ChannelModTerrainInput => "ChannelModTerrainInputProperties",
            _ => null
        };
        private static string Field(string id, string schema, string property) => Scope(id) + "/" + schema + "/Context_" + property;
        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static bool IsFiniteExtent(Extent extent) => extent.IsFiniteAndValid();
        private Extent DefaultFunctionalExtent(TerrainInputExtent? hint)
        {
            if (hint is { } captured)
            {
                Extent requested = new(captured.MinX, captured.MaxX, captured.MinY, captured.MaxY);
                if (IsFiniteExtent(requested)) return requested;
            }
            Extent fullTerrain = layer.ValidExtent;
            return IsFiniteExtent(fullTerrain) ? fullTerrain : Extent.EffectivelyInfinite;
        }
        private ITerrainInput CreateInput(string kind, string name, TerrainInputExtent? hint) => kind switch
        {
            "functionalSurface" => new FunctionalTerrainInput(layer, FunctionalTerrainInput.DefaultExpression, DefaultFunctionalExtent(hint), name),
            "polygonVectorMod" => new PolygonRasterModTerrainInput(layer, name),
            "channelVectorMod" => new ChannelModTerrainInput(layer, name),
            "simpleCutFill" => new LeveeModTerrainInput(layer, name),
            _ => throw new ArgumentException("Unknown terrain input creation kind.")
        };
        private static Extent? InputValidExtent(ITerrainInput input) => input switch
        {
            ConstantTerrainInput constant => constant.ValidExtent,
            FunctionalTerrainInput functional => functional.ValidExtent,
            RasterTerrainInput raster => ((ITerrainInput)raster).GetResampler()?.ValidExtent,
            PolygonRasterModTerrainInput polygon => polygon.ValidExtent,
            ChannelModTerrainInput channel => channel.ValidExtent,
            LeveeModTerrainInput levee => levee.ValidExtent,
            _ => null
        };
        // Retain all original dependency hashes even after an input is removed:
        // stale external data must not silently rebase the original draft.
        private Dictionary<string, string> ReadDependencies() => inputs.Values.OfType<RasterTerrainInput>().Select(x => SourceFilePaths.Canonical(x.Filename)).Concat(dependencies.Keys).Distinct(StringComparer.Ordinal).ToDictionary(p => p, p => File.Exists(p) ? Revision(p) : "missing", StringComparer.Ordinal);
        private static bool SameDependencies(Dictionary<string, string> expected, Dictionary<string, string> current) =>
            expected.Count == current.Count && expected.All(x => current.TryGetValue(x.Key, out string? value) && value == x.Value);
        public void VerifyDependencies() { if (!SameDependencies(dependencies, ReadDependencies())) throw new SourceFileChangedException(); }
        public string State() => JsonSerializer.Serialize(new DraftState("Terrain", asset, revision, OrderedIDs(), dependencies,
            importDraftToken, pendingImports.ToArray(), pendingInputs.Select(item => item with { Name = inputs[item.Id].Name }).ToArray(),
            CaptureChannelDrafts(), CaptureLeveeDrafts(), topologyDrafts.Count == 0 ? null : topologyDrafts,
            polygonTopologyDrafts.Count == 0 ? null : polygonTopologyDrafts,
            polygonSettings.Count == 0 ? null : polygonSettings), BackendJson.Options);
        private static string DraftNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static double ParseDraftNumber(string value) => double.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double number) ? number : throw new ArgumentException("The terrain draft contains an invalid number.");
        private static DraftLine? CaptureLine(Polyline? line)
        {
            if (line == null) return null;
            double[]? z = line.HasZ ? line.GetZBufferUnsafe().ToArray() : null;
            double[]? m = line.HasM ? line.GetMBufferUnsafe().ToArray() : null;
            return new DraftLine(line.GetPointBufferUnsafe().ToArray()
                .Select(point => new DraftPoint(point.X, point.Y)).ToArray(), line.HasZ, line.HasM,
                z?.Select(DraftNumber).ToArray(), m?.Select(DraftNumber).ToArray());
        }
        private static Polyline? RestoreLine(DraftLine? line)
        {
            if (line == null) return null;
            if (line.Points == null || line.Points.Any(point => point == null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new ArgumentException("The terrain draft contains an invalid feature line.");
            Point[] points = line.Points.Select(point => new Point(point.X, point.Y)).ToArray();
            if (line.HasZ != (line.Z != null) || line.HasM != (line.M != null)
                || (line.Z?.Length ?? 0) != (line.HasZ ? points.Length : 0)
                || (line.M?.Length ?? 0) != (line.HasM ? points.Length : 0))
                throw new ArgumentException("The terrain draft contains an invalid feature Z/M buffer.");
            double[] z = line.Z?.Select(ParseDraftNumber).ToArray() ?? [];
            double[] m = line.M?.Select(ParseDraftNumber).ToArray() ?? [];
            Polyline restored = new(points);
            if (line.HasZ && line.HasM) return new Polyline(points.ToList(), z.ToList(), m.ToList());
            if (line.HasZ) return new Polyline(points.ToList(), z.ToList());
            if (line.HasM) restored.TrackM(m);
            return restored;
        }
        private static DraftProfile CaptureProfile(Geospatial.PairedData.StationElevationProfile? profile) => profile == null
            ? new([], [])
            : new(profile.Stations.Select(DraftNumber).ToArray(), profile.Elevations.Select(DraftNumber).ToArray());
        private static Geospatial.PairedData.StationElevationProfile RestoreProfile(DraftProfile profile)
        {
            if (profile == null || profile.Stations == null || profile.Elevations == null
                || profile.Stations.Length != profile.Elevations.Length)
                throw new ArgumentException("The terrain draft contains an invalid station/elevation profile.");
            return new Geospatial.PairedData.StationElevationProfile(
                profile.Stations.Select(ParseDraftNumber), profile.Elevations.Select(ParseDraftNumber));
        }
        private Dictionary<string, ChannelDraftData> CaptureChannelDrafts() => inputs
            .Where(pair => pair.Value is ChannelModTerrainInput)
            .ToDictionary(pair => pair.Key, pair =>
            {
                var channel = (ChannelModTerrainInput)pair.Value;
                return new ChannelDraftData(DraftNumber(channel.TinMaxSegmentLength), channel.ModificationType.ToString(),
                    channel.Features.Select(feature => new ChannelFeatureDraft(feature.Name, CaptureLine(feature.Line), CaptureProfile(feature.Profile))).ToArray(),
                    channelGeometryPayloads.GetValueOrDefault(pair.Key)?.Values.OrderBy(payload => payload.Id, StringComparer.Ordinal).ToArray() ?? []);
            }, StringComparer.Ordinal);
        private Dictionary<string, LeveeDraftData> CaptureLeveeDrafts() => inputs
            .Where(pair => pair.Value is LeveeModTerrainInput)
            .ToDictionary(pair => pair.Key, pair =>
            {
                var levee = (LeveeModTerrainInput)pair.Value;
                LeveeFeatureDraft[] features = levee.Features.Select(feature => new LeveeFeatureDraft(
                    feature.Name, feature.LeveePropertyName, CaptureLine(feature.Line), CaptureProfile(feature.Profile))).ToArray();
                LeveePropertyDraft[] properties = levee.PropertiesTable.Features.Select(property => new LeveePropertyDraft(
                    property.Name, property.Mode.ToString(), DraftNumber(property.TopWidth), DraftNumber(property.LeftWidth),
                    DraftNumber(property.RightWidth), DraftNumber(property.LeftGrade), DraftNumber(property.RightGrade),
                    property.ModType.ToString())).ToArray();
                return new LeveeDraftData(features, properties);
            }, StringComparer.Ordinal);
        private void RestoreStructureDrafts(DraftState draft)
        {
            Dictionary<string, ChannelModTerrainInput> channels = inputs.Where(pair => pair.Value is ChannelModTerrainInput)
                .ToDictionary(pair => pair.Key, pair => (ChannelModTerrainInput)pair.Value, StringComparer.Ordinal);
            if (draft.ChannelDrafts != null)
            {
                if (draft.ChannelDrafts.Count != channels.Count || draft.ChannelDrafts.Keys.Any(id => !channels.ContainsKey(id)))
                    throw new ArgumentException("The terrain channel draft does not match its source inputs.");
                foreach (var pair in draft.ChannelDrafts)
                {
                    ChannelDraftData data = pair.Value ?? throw new ArgumentException("The terrain channel draft is incomplete.");
                    ChannelModTerrainInput channel = channels[pair.Key];
                    double length = ParseDraftNumber(data.TinMaxSegmentLength);
                    if (!double.IsFinite(length) || length <= 0 || !Enum.TryParse(data.ModificationType, false, out Geospatial.Rasters.RasterModType modificationType)
                        || !Enum.IsDefined(modificationType) || data.Features == null)
                        throw new ArgumentException("The terrain channel draft contains invalid settings.");
                    ChannelProfile[] features = data.Features.Select(feature => feature == null
                        ? throw new ArgumentException("The terrain channel draft contains an empty feature.")
                        : new ChannelProfile(RestoreLine(feature.Line)!, RestoreProfile(feature.Profile)) { Name = feature.Name ?? "" }).ToArray();
                    channel.TinMaxSegmentLength = length;
                    channel.ModificationType = modificationType;
                    channel.Clear();
                    channel.AddRange(features);
                    TerrainChannelGeometryPayload[] imported = data.ImportedGeometryPayloads ?? [];
                    foreach (TerrainChannelGeometryPayload payload in imported)
                    {
                        TerrainChannelGeometryPayloadCodec.Validate(payload);
                        if (importDraftToken == null)
                            throw new ArgumentException("The imported Channel geometry is missing its owned terrain draft directory.");
                        _ = TerrainChannelGeometryPayloadCodec.VerifyFile(asset, importDraftToken, payload);
                    }
                    if (imported.Select(payload => payload.Id).Distinct(StringComparer.Ordinal).Count() != imported.Length)
                        throw new ArgumentException("The terrain channel draft repeats a migrated geometry payload.");
                    channelGeometryPayloads[pair.Key] = imported.ToDictionary(payload => payload.Id, StringComparer.Ordinal);
                }
            }

            Dictionary<string, LeveeModTerrainInput> levees = inputs.Where(pair => pair.Value is LeveeModTerrainInput)
                .ToDictionary(pair => pair.Key, pair => (LeveeModTerrainInput)pair.Value, StringComparer.Ordinal);
            if (draft.LeveeDrafts != null)
            {
                if (draft.LeveeDrafts.Count != levees.Count || draft.LeveeDrafts.Keys.Any(id => !levees.ContainsKey(id)))
                    throw new ArgumentException("The terrain levee draft does not match its source inputs.");
                foreach (var pair in draft.LeveeDrafts)
                {
                    LeveeDraftData data = pair.Value ?? throw new ArgumentException("The terrain levee draft is incomplete.");
                    if (data.Features == null || data.Properties == null) throw new ArgumentException("The terrain levee draft is incomplete.");
                    LeveeModTerrainInput levee = levees[pair.Key];
                    LeveeModProperties[] properties = data.Properties.Select(item =>
                    {
                        if (item == null || !Enum.TryParse(item.Mode, false, out LeveeMode mode) || !Enum.IsDefined(mode)
                            || !Enum.TryParse(item.ModificationType, false, out Geospatial.Rasters.RasterModType modificationType)
                            || !Enum.IsDefined(modificationType))
                            throw new ArgumentException("The terrain levee draft contains invalid property settings.");
                        return new LeveeModProperties
                        {
                            Name = item.Name ?? "", Mode = mode, TopWidth = ParseDraftNumber(item.TopWidth),
                            LeftWidth = ParseDraftNumber(item.LeftWidth), RightWidth = ParseDraftNumber(item.RightWidth),
                            LeftGrade = ParseDraftNumber(item.LeftGrade), RightGrade = ParseDraftNumber(item.RightGrade), ModType = modificationType
                        };
                    }).ToArray();
                    LeveeMod[] features = data.Features.Select(feature => feature == null
                        ? throw new ArgumentException("The terrain levee draft contains an empty feature.")
                        : new LeveeMod(RestoreLine(feature.Line)!, RestoreProfile(feature.Profile))
                        { Name = feature.Name ?? "", LeveePropertyName = feature.PropertyName ?? "" }).ToArray();
                    levee.PropertiesTable.Clear();
                    levee.PropertiesTable.AddRange(properties);
                    levee.Clear();
                    levee.AddRange(features);
                }
            }
        }
        private void RestoreTopologyDrafts(Dictionary<string, TerrainTopologyDraft> restored)
        {
            if (restored.Any(pair => pair.Value is null || !topologyBaselines.ContainsKey(pair.Key)))
                throw new ArgumentException("The terrain topology draft does not match its channel inputs.");
            foreach (var pair in restored)
            {
                TerrainTopologyMutation[] mutations = pair.Value.Mutations ?? throw new ArgumentException("The terrain topology draft is incomplete.");
                TerrainTopologyMutation[] redo = pair.Value.RedoMutations ?? [];
                ValidateTopologyMutationHistory(mutations);
                ValidateTopologyMutationHistory(redo);
                foreach (TerrainTopologyMutation mutation in mutations.Concat(redo).Where(mutation => mutation.Operation == "importGeometry"))
                {
                    if (!channelGeometryPayloads.TryGetValue(pair.Key, out Dictionary<string, TerrainChannelGeometryPayload>? payloads)
                        || mutation.ImportPayloadID is not { } payloadID || !payloads.TryGetValue(payloadID, out TerrainChannelGeometryPayload? payload)
                        || !StringComparer.Ordinal.Equals(mutation.ImportSourceSHA256, payload.SourceSHA256))
                        throw new ArgumentException("The terrain topology import history no longer has its exact geometry payload.");
                }
                if (mutations.Length != 0 || redo.Length != 0) topologyDrafts.Add(pair.Key, pair.Value with { Mutations = mutations, RedoMutations = redo });
            }
            foreach (string inputID in topologyDrafts.Keys.ToArray()) RebuildTopology(inputID);
        }

        private static bool IsSourceVectorInput(ITerrainInput input) => input is ChannelModTerrainInput
            or PolygonRasterModTerrainInput or LeveeModTerrainInput
            || string.Equals(input.GetType().Name, "EllipseRasterModificationLayer", StringComparison.Ordinal);

        private static PolygonRasterMod[] CapturePolygonBaseline(PolygonRasterModTerrainInput input) =>
            input.Features.Select(feature => new PolygonRasterMod(
                feature.Polygon is null ? throw new InvalidDataException("A terrain source polygon is missing.")
                    : new Polygon(feature.Polygon), feature.ModificationType, feature.Value)).ToArray();

        private static void ValidatePolygonSettings(TerrainPolygonEditSettings? settings)
        {
            if (settings is null || settings.Shape is not ("Triangle" or "Rectangle" or "Custom") ||
                !double.IsFinite(settings.Width) || settings.Width <= 0 ||
                !double.IsFinite(settings.Height) || settings.Height <= 0 ||
                !double.IsFinite(settings.Rotation) || !float.IsFinite(settings.Value) ||
                !Enum.TryParse(settings.ModificationType, false, out Geospatial.Rasters.RasterModType type) ||
                !Enum.IsDefined(type))
                throw new ArgumentException("The terrain polygon settings are invalid.");
        }

        private TerrainPolygonEditSettings PolygonSettingsFor(string inputID)
        {
            if (polygonSettings.TryGetValue(inputID, out TerrainPolygonEditSettings? settings)) return settings;
            return new TerrainPolygonEditSettings("Triangle", 500, 500, 0, 1000, "SetValue");
        }

        private void RestorePolygonTopologyDrafts(Dictionary<string, TerrainPolygonTopologyDraft> restored)
        {
            if (restored.Any(pair => pair.Value is null || !polygonBaselines.ContainsKey(pair.Key)))
                throw new ArgumentException("The terrain polygon journal does not match a saved source polygon input.");
            foreach (var pair in restored)
            {
                TerrainPolygonTopologyMutation[] mutations = pair.Value.Mutations
                    ?? throw new ArgumentException("The terrain polygon journal is incomplete.");
                TerrainPolygonTopologyMutation[] redo = pair.Value.RedoMutations
                    ?? throw new ArgumentException("The terrain polygon redo journal is incomplete.");
                ValidatePolygonMutationHistory(mutations);
                ValidatePolygonMutationHistory(redo);
                if (mutations.Length != 0 || redo.Length != 0)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value.InputNodeID) || pair.Value.InputIndex is not int inputIndex ||
                        !sourceVectorIndices.TryGetValue(pair.Key, out int currentIndex) || currentIndex != inputIndex ||
                        !inputs.TryGetValue(pair.Key, out ITerrainInput? sourceInput) ||
                        sourceInput is not PolygonRasterModTerrainInput ||
                        !sourcePolygonNodeIDs.TryGetValue(pair.Key, out string? originalSourceNodeID) ||
                        !string.Equals(pair.Value.InputNodeID, originalSourceNodeID, StringComparison.Ordinal))
                        throw new SourceFileChangedException();
                    polygonTopologyDrafts.Add(pair.Key, pair.Value with { Mutations = mutations, RedoMutations = redo });
                }
            }
            foreach (string inputID in polygonTopologyDrafts.Keys.ToArray()) _ = RebuildPolygonTopology(inputID);
        }

        private static void ValidatePolygonMutationHistory(TerrainPolygonTopologyMutation[] mutations)
        {
            foreach (TerrainPolygonTopologyMutation mutation in mutations)
            {
                if (mutation is null || mutation.FeatureIDs is null ||
                    mutation.FeatureIDs.Any(string.IsNullOrWhiteSpace) ||
                    mutation.FeatureIDs.Distinct(StringComparer.Ordinal).Count() != mutation.FeatureIDs.Length ||
                    mutation.Points?.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) == true)
                    throw new ArgumentException("The terrain polygon journal contains an invalid feature identity or coordinate.");
                bool valid = mutation.Operation switch
                {
                    "stamp" => mutation.FeatureIDs.Length == 0 && mutation.Points is { Length: 1 } && mutation.Settings is not null,
                    "draw" => mutation.FeatureIDs.Length == 0 && mutation.Points is { Length: >= 3 } && mutation.Settings is not null,
                    "remove" => mutation.FeatureIDs.Length > 0 && mutation.Points is null && mutation.Settings is null,
                    _ => false
                };
                if (!valid) throw new ArgumentException("The terrain polygon journal contains an invalid operation.");
                if (mutation.Settings is not null) ValidatePolygonSettings(mutation.Settings);
            }
        }

        private TerrainPolygonTopologyEditSession RebuildPolygonTopology(string inputID,
            string? currentInputNodeID = null, int? currentInputIndex = null)
        {
            if (!polygonBaselines.TryGetValue(inputID, out PolygonRasterMod[]? baseline) ||
                !inputs.TryGetValue(inputID, out ITerrainInput? input) || input is not PolygonRasterModTerrainInput polygon)
                throw new ArgumentException("The selected terrain polygon input is not a saved source layer.");
            if (!sourceVectorIndices.TryGetValue(inputID, out int sourceInputIndex))
                throw new ArgumentException("Only a saved source polygon input can be edited on the map.");
            if (currentInputIndex is int requestedIndex && requestedIndex != sourceInputIndex)
                throw new SourceFileChangedException();
            TerrainPolygonTopologyDraft? draft = polygonTopologyDrafts.GetValueOrDefault(inputID);
            string nodeID = currentInputNodeID ?? draft?.InputNodeID
                ?? throw new ArgumentException("The terrain polygon map source is unavailable.");
            if (!sourcePolygonNodeIDs.TryGetValue(inputID, out string? expectedNodeID) ||
                !string.Equals(nodeID, expectedNodeID, StringComparison.Ordinal) ||
                (draft is not null && (draft.InputNodeID != nodeID || draft.InputIndex != sourceInputIndex)))
                throw new SourceFileChangedException();
            polygon.Clear();
            polygon.AddRange(baseline.Select(feature => new PolygonRasterMod(new Polygon(feature.Polygon),
                feature.ModificationType, feature.Value)));
            var session = new TerrainPolygonTopologyEditSession(nodeID, polygon, observableTerrain);
            if (draft is not null)
                foreach (TerrainPolygonTopologyMutation mutation in draft.Mutations) session.Apply(mutation);
            polygonTopologySessions[inputID] = session;
            return session;
        }

        private readonly Dictionary<string, TerrainPolygonTopologyEditSession> polygonTopologySessions = new(StringComparer.Ordinal);
        private TerrainTopologyData PolygonTopologyData(string inputID, TerrainPolygonTopologyDraft draft,
            TerrainPolygonTopologyEditSession session, TerrainInputMapContextRequest mapContext,
            string[] selectedFeatureIDs)
        {
            int inputIndex = draft.InputIndex ?? throw new SourceFileChangedException();
            string nodeID = draft.InputNodeID ?? throw new SourceFileChangedException();
            TerrainTopologyFeature[] sourceFeatures = session.Features(points => points);
            TerrainTopologyFeature[] mapFeatures = sourceFeatures.Select(feature =>
            {
                TerrainMapCoordinate[] mapPoints = TerrainInputMapContextService.ToProjectMapCoordinates(
                    mapContext, inputIndex, revision, feature.Points);
                return feature with { Points = mapPoints, Rings = [mapPoints] };
            }).ToArray();
            string encoded = JsonSerializer.Serialize(draft, BackendJson.Options);
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                asset + "\n" + revision + "\n" + inputID + "\n" + encoded)));
            return new TerrainTopologyData(inputID, revision, identity, mapFeatures, selectedFeatureIDs,
                draft.Mutations.Length > 0, draft.RedoMutations.Length > 0, inputIndex,
                polygonSettings.GetValueOrDefault(inputID) ?? PolygonSettingsFor(inputID));
        }

        private TerrainTopologyData ApplyPolygonTopologyOperation(TerrainTopologyRequest request,
            string[] featureIDs, TerrainMapCoordinate[] mapPoints)
        {
            TerrainInputMapContextRequest mapContext = request.MapContext
                ?? throw new ArgumentException("Map editing requires an exact current TerrainInput map capture.");
            if (mapContext.ExpectedInputIndex is not int expectedIndex ||
                mapContext.ExpectedTerrainRevision is not { Length: 64 } expectedRevision ||
                expectedRevision.Any(character => !Uri.IsHexDigit(character)) ||
                mapContext.ExpectedSourceClass != "PolygonRasterModTerrainInputVisualContext" ||
                !sourceVectorIndices.TryGetValue(request.InputId, out int sourceIndex) || sourceIndex != expectedIndex ||
                !PathComparer.Equals(SourceFilePaths.Canonical(asset), SourceFilePaths.Canonical(mapContext.ExpectedTerrainPath ?? string.Empty)) ||
                !PathComparer.Equals(SourceFilePaths.Canonical(request.Model.ProjectPath ?? string.Empty),
                    SourceFilePaths.Canonical(mapContext.Map.ProjectPath)))
                throw new SourceFileChangedException();

            TerrainInputMapContextData capture = TerrainInputMapContextService.Render(mapContext);
            if (capture.InputIndex != sourceIndex ||
                !string.Equals(capture.TerrainRevision, expectedRevision, StringComparison.OrdinalIgnoreCase) ||
                !PathComparer.Equals(SourceFilePaths.Canonical(capture.TerrainPath), SourceFilePaths.Canonical(asset)))
                throw new SourceFileChangedException();

            TerrainPolygonTopologyDraft draft = polygonTopologyDrafts.GetValueOrDefault(request.InputId)
                ?? new TerrainPolygonTopologyDraft([]) { InputNodeID = mapContext.InputNodeID, InputIndex = sourceIndex };
            if (draft.InputNodeID != mapContext.InputNodeID || draft.InputIndex != sourceIndex)
                throw new SourceFileChangedException();
            TerrainPolygonTopologyEditSession session = RebuildPolygonTopology(request.InputId,
                mapContext.InputNodeID, sourceIndex);
            if (request.Operation != "load")
            {
                string expectedDraft = request.ExpectedDraftIdentity
                    ?? throw new ArgumentException("The terrain polygon command is missing its captured draft identity.");
                string currentDraft = PolygonTopologyData(request.InputId, draft, session, mapContext, []).DraftIdentity;
                if (!StringComparer.Ordinal.Equals(expectedDraft, currentDraft))
                    throw new ArgumentException("The terrain polygon draft changed before the command.");
            }

            TerrainTopologyFeature[] sourceFeatures = session.Features(points => points);
            HashSet<string> availableIDs = sourceFeatures.Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
            if (featureIDs.Any(id => !availableIDs.Contains(id)))
                throw new ArgumentException("A selected terrain polygon no longer exists.");
            string[] selectedFeatureIDs = [];

            if (request.Operation is "load" or "undo" or "redo")
            {
                if (featureIDs.Length != 0 || mapPoints.Length != 0 || request.PolygonSettings is not null)
                    throw new ArgumentException("A terrain polygon history command cannot include feature or point data.");
            }
            switch (request.Operation)
            {
                case "load":
                    if (selectedRows.TryGetValue(PolygonFeatureTable, out string[]? selectedTableRows))
                    {
                        session.Select(selectedTableRows);
                        selectedFeatureIDs = session.SelectedFeatureIDs();
                    }
                    break;
                case "select":
                    if (mapPoints.Length != 0 || request.PolygonSettings is not null)
                        throw new ArgumentException("Polygon selection cannot include point or placement settings.");
                    session.Select(featureIDs);
                    selectedFeatureIDs = featureIDs;
                    break;
                case "selectAt":
                    if (featureIDs.Length != 0 || mapPoints.Length != 1 || request.MapCellSize is not double mapCellSize ||
                        !double.IsFinite(mapCellSize) || mapCellSize <= 0 || request.PolygonSettings is not null)
                        throw new ArgumentException("Map selection needs one point and a positive captured cell size.");
                    TerrainMapCoordinate sourcePoint = TerrainInputMapContextService.ToTerrainSourceCoordinates(
                        mapContext, sourceIndex, expectedRevision, mapPoints)[0];
                    double sourceCellSize = TerrainInputMapContextService.ToTerrainSourceCellSize(
                        mapContext, sourceIndex, expectedRevision, mapPoints[0], mapCellSize);
                    selectedFeatureIDs = session.SelectAt(sourcePoint, sourceCellSize);
                    break;
                case "stamp":
                    if (featureIDs.Length != 0 || mapPoints.Length != 1 || request.MapCellSize is not double stampCellSize ||
                        !double.IsFinite(stampCellSize) || stampCellSize <= 0 || request.PolygonSettings is null)
                        throw new ArgumentException("Polygon Stamp needs one map anchor and valid shape settings.");
                    ValidatePolygonSettings(request.PolygonSettings);
                    TerrainMapCoordinate sourceAnchor = TerrainInputMapContextService.ToTerrainSourceCoordinates(
                        mapContext, sourceIndex, expectedRevision, mapPoints)[0];
                    _ = session.Stamp(sourceAnchor, stampCellSize, request.PolygonSettings);
                    draft = draft with { Mutations = [.. draft.Mutations,
                        new TerrainPolygonTopologyMutation("stamp", [], [sourceAnchor], request.PolygonSettings)], RedoMutations = [] };
                    selectedFeatureIDs = session.SelectedFeatureIDs();
                    polygonSettings[request.InputId] = request.PolygonSettings;
                    break;
                case "draw":
                    if (featureIDs.Length != 0 || mapPoints.Length < 3 || request.MapCellSize is not null ||
                        request.PolygonSettings is null)
                        throw new ArgumentException("Polygon Draw needs at least three map vertices and valid shape settings.");
                    ValidatePolygonSettings(request.PolygonSettings);
                    TerrainMapCoordinate[] sourcePoints = TerrainInputMapContextService.ToTerrainSourceCoordinates(
                        mapContext, sourceIndex, expectedRevision, mapPoints);
                    _ = session.Draw(sourcePoints, request.PolygonSettings);
                    draft = draft with { Mutations = [.. draft.Mutations,
                        new TerrainPolygonTopologyMutation("draw", [], sourcePoints, request.PolygonSettings)], RedoMutations = [] };
                    selectedFeatureIDs = session.SelectedFeatureIDs();
                    polygonSettings[request.InputId] = request.PolygonSettings;
                    break;
                case "remove":
                    if (featureIDs.Length == 0 || mapPoints.Length != 0 || request.MapCellSize is not null ||
                        request.PolygonSettings is not null)
                        throw new ArgumentException("Remove requires one or more selected terrain polygons.");
                    session.Remove(featureIDs);
                    draft = draft with { Mutations = [.. draft.Mutations,
                        new TerrainPolygonTopologyMutation("remove", featureIDs)], RedoMutations = [] };
                    break;
                case "undo":
                    if (draft.Mutations.Length > 0)
                    {
                        TerrainPolygonTopologyMutation undone = draft.Mutations[^1];
                        draft = draft with { Mutations = draft.Mutations[..^1], RedoMutations = [.. draft.RedoMutations, undone] };
                    }
                    break;
                case "redo":
                    if (draft.RedoMutations.Length > 0)
                    {
                        TerrainPolygonTopologyMutation redone = draft.RedoMutations[^1];
                        draft = draft with { Mutations = [.. draft.Mutations, redone], RedoMutations = draft.RedoMutations[..^1] };
                    }
                    break;
                default:
                    throw new ArgumentException("Unknown terrain polygon map operation.");
            }

            if (draft.Mutations.Length == 0 && draft.RedoMutations.Length == 0)
                polygonTopologyDrafts.Remove(request.InputId);
            else
                polygonTopologyDrafts[request.InputId] = draft;
            if (request.Operation is "undo" or "redo")
            {
                session = RebuildPolygonTopology(request.InputId, mapContext.InputNodeID, sourceIndex);
                selectedFeatureIDs = session.SelectedFeatureIDs();
            }
            else
                polygonTopologySessions[request.InputId] = session;

            // The feature table and map use the same IDs returned by the
            // retained topology session. This keeps map hit-tests, table rows,
            // undo/redo replays, and selection snapshots on one identity path.
            string[] currentFeatureIDs = session.Features(points => points).Select(feature => feature.Id).ToArray();
            if (selectedFeatureIDs.Any(id => !currentFeatureIDs.Contains(id, StringComparer.Ordinal)))
                throw new ArgumentException("The selected terrain polygon no longer exists.");
            selectedRows[PolygonFeatureTable] = selectedFeatureIDs.ToArray();
            return PolygonTopologyData(request.InputId, draft, session, mapContext, selectedFeatureIDs);
        }

        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private static void ValidateTopologyMutationHistory(TerrainTopologyMutation[] mutations)
        {
            foreach (TerrainTopologyMutation item in mutations)
            {
                if (item is null || item.FeatureIDs == null || item.FeatureIDs.Any(string.IsNullOrWhiteSpace)
                    || item.FeatureIDs.Distinct(StringComparer.Ordinal).Count() != item.FeatureIDs.Length
                    || item.Points?.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) == true)
                    throw new ArgumentException("The terrain topology draft contains an invalid graph command.");
                bool hasSize = item.MapCellSize is double size && double.IsFinite(size) && size > 0;
                bool noMetadata = item.MetadataKind == null && item.MetadataColumn == null && item.MetadataValue == null;
                bool noImport = item.ImportPayloadID == null && item.ImportSourceSHA256 == null
                    && item.ImportedChannelProfiles == null;
                bool valid = item.Operation switch
                {
                    "draw" => item.FeatureIDs.Length == 0 && item.Points is { Length: >= 2 } && hasSize
                        && item.SplitMode == null && item.VertexIndex == null && noMetadata && noImport,
                    "remove" => item.FeatureIDs.Length > 0 && item.Points == null && hasSize
                        && item.SplitMode == null && item.VertexIndex == null && noMetadata && noImport,
                    "split" => item.FeatureIDs.Length == 1 && item.Points is { Length: 1 } && hasSize
                        && item.SplitMode is ("Anywhere" or "AtVertex") && item.VertexIndex == null && noMetadata && noImport,
                    "moveNodes" => item.FeatureIDs.Length > 0 && item.Points is { Length: 2 } && hasSize
                        && item.SplitMode == null && item.VertexIndex == null && noMetadata && noImport,
                    "moveVertex" => item.FeatureIDs.Length == 1 && item.Points is { Length: 2 } && hasSize
                        && item.VertexIndex is >= 1 && item.SplitMode == null && noMetadata && noImport,
                    "insertVertex" => item.FeatureIDs.Length == 1 && item.Points is { Length: 1 } && hasSize
                        && item.SplitMode == null && item.VertexIndex == null && noMetadata && noImport,
                    "deleteVertex" => item.FeatureIDs.Length == 1 && item.Points == null && item.MapCellSize == null
                        && item.VertexIndex is >= 1 && item.SplitMode == null && noMetadata && noImport,
                    "maskRegion" => item.FeatureIDs.Length == 1 && item.Points == null && item.MapCellSize == null
                        && item.SplitMode == null && item.VertexIndex == null && noMetadata && noImport,
                    "setMetadata" => item.FeatureIDs.Length == 1 && item.Points == null && item.MapCellSize == null
                        && item.SplitMode == null && item.VertexIndex == null
                        && item.MetadataKind is ("arc" or "node" or "controlPoint")
                        && !string.IsNullOrWhiteSpace(item.MetadataColumn) && item.MetadataValue != null && noImport,
                    "importGeometry" => item.FeatureIDs.Length == 0 && item.Points == null && item.MapCellSize == null
                        && item.SplitMode == null && item.VertexIndex == null && noMetadata
                        && item.ImportPayloadID is { Length: 32 } payloadID && Guid.TryParseExact(payloadID, "N", out _)
                        && item.ImportSourceSHA256 is { Length: 64 } sourceSHA
                        && sourceSHA.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F')
                        && item.ImportedChannelProfiles is { Length: > 0 } importedProfiles
                        && importedProfiles.All(IsValidImportedChannelProfile),
                    _ => false
                };
                if (!valid) throw new ArgumentException("The terrain topology draft contains an invalid graph command.");
            }
        }
        private static bool IsValidImportedChannelProfile(TerrainImportedChannelProfileSnapshot? profile)
        {
            if (profile is null || profile.Points is not { Length: >= 2 } points
                || points.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
                || profile.Stations is null || profile.Elevations is null
                || profile.Stations.Length != profile.Elevations.Length
                || profile.HasZ != (profile.Z != null) || profile.HasM != (profile.M != null)
                || profile.Z is { } z && z.Length != points.Length
                || profile.M is { } m && m.Length != points.Length)
                return false;
            return profile.Stations.All(IsDraftNumber) && profile.Elevations.All(IsDraftNumber)
                && (profile.Z?.All(IsDraftNumber) ?? true) && (profile.M?.All(IsDraftNumber) ?? true);
        }
        private static bool IsDraftNumber(string? value) => value != null
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        private static TerrainImportedChannelProfileSnapshot[] CaptureImportedChannelProfiles(IEnumerable<ChannelProfile> features) => features
            .Select(feature =>
            {
                DraftLine line = CaptureLine(feature.Line) ?? throw new InvalidDataException("An imported Channel profile has no source line.");
                DraftProfile profile = CaptureProfile(feature.Profile);
                return new TerrainImportedChannelProfileSnapshot(feature.Name,
                    line.Points.Select(point => new TerrainMapCoordinate(point.X, point.Y)).ToArray(),
                    line.HasZ, line.HasM, line.Z, line.M, profile.Stations, profile.Elevations);
            }).ToArray();
        private static void RestoreImportedChannelProfiles(ChannelModTerrainInput channel,
            TerrainImportedChannelProfileSnapshot[] profiles)
        {
            if (profiles is not { Length: > 0 } || profiles.Any(profile => !IsValidImportedChannelProfile(profile)))
                throw new ArgumentException("The imported Channel profile snapshot is invalid.");
            ChannelProfile[] restored = profiles.Select(profile => new ChannelProfile(
                RestoreLine(new DraftLine(profile.Points.Select(point => new DraftPoint(point.X, point.Y)).ToArray(),
                    profile.HasZ, profile.HasM, profile.Z, profile.M))!,
                RestoreProfile(new DraftProfile(profile.Stations, profile.Elevations)))
            { Name = profile.Name ?? string.Empty }).ToArray();
            channel.Clear();
            channel.AddRange(restored);
        }
        private TerrainTopologyEditSession RebuildTopology(string inputID)
        {
            if (!topologyBaselines.TryGetValue(inputID, out var baseline)
                || !inputs.TryGetValue(inputID, out ITerrainInput? input) || input is not ChannelModTerrainInput channel)
                throw new ArgumentException("The selected channel topology no longer exists.");
            channel.TopologyLayer.Topology = baseline.Clone();
            var session = new TerrainTopologyEditSession(inputID, channel);
            session.Initialize();
            if (topologyDrafts.TryGetValue(inputID, out TerrainTopologyDraft? draft))
                foreach (TerrainTopologyMutation mutation in draft.Mutations)
                {
                    if (mutation.Operation == "importGeometry")
                    {
                        if (!channelGeometryPayloads.TryGetValue(inputID, out Dictionary<string, TerrainChannelGeometryPayload>? payloads)
                            || mutation.ImportPayloadID is not { } payloadID || !payloads.TryGetValue(payloadID, out TerrainChannelGeometryPayload? payload)
                            || !StringComparer.Ordinal.Equals(payload.SourceSHA256, mutation.ImportSourceSHA256))
                            throw new ArgumentException("The imported Channel geometry payload is missing or changed.");
                        if (importDraftToken == null)
                            throw new ArgumentException("The imported Channel geometry is missing its owned terrain draft directory.");
                        TerrainChannelGeometryPayloadCodec.WithClassicGeometry(asset, importDraftToken, payload, geometry =>
                        {
                            if (geometry.CrossSectionLayer.Count == 0)
                                throw new InvalidDataException("The converted geometry has no cross sections to import.");
                            channel.Import1DGeometryTopology(geometry);
                            return true;
                        });
                        session = new TerrainTopologyEditSession(inputID, channel);
                        session.Initialize();
                    }
                    else session.Apply(mutation);
                }
            return session;
        }
        public TerrainTopologyData LoadChannelTopology(string inputID)
        {
            if (selected != inputID || !inputs.TryGetValue(inputID, out ITerrainInput? input)
                || input is not ChannelModTerrainInput)
                throw new ArgumentException("Select the exact Channel modification input before importing Version 6 geometry.");
            TerrainTopologyEditSession session = RebuildTopology(inputID);
            return TopologyData(inputID, session.Features(), []);
        }
        public TerrainTopologyData ImportChannelV6Geometry(string inputID, string migratedPath,
            string sourceSHA256, string expectedDraftIdentity, out string stagedPayloadPath)
        {
            if (selected != inputID || !inputs.TryGetValue(inputID, out ITerrainInput? input)
                || input is not ChannelModTerrainInput channel || channel.Features.Count != 0)
                throw new ArgumentException("Version 6 geometry can only be imported into the selected empty Channel modification input.");
            TerrainTopologyData current = LoadChannelTopology(inputID);
            if (!StringComparer.Ordinal.Equals(current.DraftIdentity, expectedDraftIdentity))
                throw new SourceFileChangedException();
            string? existingToken = importDraftToken;
            string draftToken = existingToken ?? Guid.NewGuid().ToString("N");
            string? stagedPath = null;
            stagedPayloadPath = string.Empty;
            try
            {
                TerrainChannelGeometryPayload payload = TerrainChannelGeometryPayloadCodec.Stage(asset, draftToken, migratedPath, sourceSHA256);
                stagedPath = TerrainChannelGeometryPayloadCodec.PathFor(asset, draftToken, payload.FileName);
                stagedPayloadPath = stagedPath;
                TerrainChannelGeometryPayloadCodec.WithClassicGeometry(asset, draftToken, payload, geometry =>
                {
                    if (geometry.CrossSectionLayer.Count == 0)
                        throw new InvalidDataException("The converted geometry has no cross sections to import.");
                    channel.Import1DGeometry(geometry);
                    return true;
                });
                if (existingToken == null) importDraftToken = draftToken;
                if (!channelGeometryPayloads.TryGetValue(inputID, out Dictionary<string, TerrainChannelGeometryPayload>? payloads))
                    channelGeometryPayloads[inputID] = payloads = new(StringComparer.Ordinal);
                payloads.Add(payload.Id, payload);
                TerrainTopologyMutation mutation = new("importGeometry", [], ImportPayloadID: payload.Id,
                    ImportSourceSHA256: payload.SourceSHA256,
                    ImportedChannelProfiles: CaptureImportedChannelProfiles(channel.Features));
                ValidateTopologyMutationHistory([mutation]);
                TerrainTopologyDraft draft = topologyDrafts.GetValueOrDefault(inputID) ?? new TerrainTopologyDraft([]);
                topologyDrafts[inputID] = draft with { Mutations = [.. draft.Mutations, mutation], RedoMutations = [] };
                TerrainTopologyEditSession updated = RebuildTopology(inputID);
                return TopologyData(inputID, updated.Features(), []);
            }
            catch
            {
                if (stagedPath is not null && File.Exists(stagedPath)) File.Delete(stagedPath);
                stagedPayloadPath = string.Empty;
                if (existingToken == null)
                {
                    importDraftToken = null;
                    string directory = TerrainInputImportService.DraftDirectory(asset, draftToken);
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                }
                throw;
            }
        }
        public TerrainTopologyData ApplyTopologyOperation(TerrainTopologyRequest request)
        {
            if (selected != request.InputId || !inputs.TryGetValue(request.InputId, out ITerrainInput? input))
                throw new ArgumentException("The selected terrain input changed before the topology command.");
            string[] featureIDs = request.FeatureIDs ?? [];
            TerrainMapCoordinate[] points = request.Points ?? [];
            if (featureIDs.Any(string.IsNullOrWhiteSpace) || featureIDs.Distinct(StringComparer.Ordinal).Count() != featureIDs.Length
                || points.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new ArgumentException("The terrain topology command contains invalid feature identities or coordinates.");
            if (request.Operation == "sortProfile")
            {
                if (input is not LeveeModTerrainInput levee || featureIDs.Length != 1 || points.Length != 0 || request.MapCellSize is not null
                    || request.ExpectedDraftIdentity != null || request.SplitMode != null || request.VertexIndex != null
                    || request.MetadataKind != null || request.MetadataColumn != null || request.MetadataValue != null
                    || !selectedRows.TryGetValue(LeveeFeatureTable, out string[]? selectedLeveeIDs)
                    || selectedLeveeIDs.Length != 1 || selectedLeveeIDs[0] != featureIDs[0])
                    throw new ArgumentException("Sort Stations requires one selected levee feature.");
                string[] rowIDs = RowIDsFor(request.InputId, LeveeFeatureTable);
                int index = Array.IndexOf(rowIDs, featureIDs[0]);
                if (index < 0 || index >= levee.Features.Count) throw new ArgumentException("The selected levee feature no longer exists.");
                levee.Features[index].Profile.SortByStation();
                Polyline? line = levee.Features[index].Line;
                TerrainMapCoordinate[] path = line == null ? [] : line.GetPointBufferUnsafe().ToArray()
                    .Select(point => new TerrainMapCoordinate(point.X, point.Y)).ToArray();
                TerrainTopologyFeature[] leveeFeatures = [new TerrainTopologyFeature(featureIDs[0], "line", path)];
                return TopologyData(request.InputId, leveeFeatures, [featureIDs[0]]);
            }
            if (input is PolygonRasterModTerrainInput)
                return ApplyPolygonTopologyOperation(request, featureIDs, points);
            if (request.MapContext is not null || request.PolygonSettings is not null)
                throw new ArgumentException("Map polygon context can only be used by a saved polygon modification input.");
            if (input is not ChannelModTerrainInput)
                throw new ArgumentException("Channel topology operations require a channel modification input.");
            if (request.Operation is not ("load" or "select" or "draw" or "remove" or "undo" or "redo"
                or "split" or "moveNodes" or "moveVertex" or "insertVertex" or "deleteVertex" or "maskRegion" or "setMetadata"))
                throw new ArgumentException("Unknown terrain topology operation.");
            TerrainTopologyEditSession session = RebuildTopology(request.InputId);
            TerrainTopologyDraft draft = topologyDrafts.GetValueOrDefault(request.InputId) ?? new TerrainTopologyDraft([]);
            if (request.Operation != "load")
            {
                string expected = request.ExpectedDraftIdentity
                    ?? throw new ArgumentException("The topology command is missing its captured draft identity.");
                string current = TopologyData(request.InputId, session.Features(), []).DraftIdentity;
                if (!StringComparer.Ordinal.Equals(expected, current))
                    throw new ArgumentException("The channel topology draft changed before the command.");
            }
            if (request.Operation is "load" or "undo" or "redo")
            {
                if (featureIDs.Length != 0 || points.Length != 0 || request.MapCellSize is not null
                    || request.SplitMode != null || request.VertexIndex != null || request.MetadataKind != null
                    || request.MetadataColumn != null || request.MetadataValue != null)
                    throw new ArgumentException("A terrain topology history command cannot include feature or point data.");
            }
            string[] selectedFeatureIDs = [];
            TerrainTopologyFeature[] features;
            if (request.Operation == "load")
            {
                features = session.Features();
            }
            else if (request.Operation == "select")
            {
                if (points.Length != 0 || request.MapCellSize is not null || request.SplitMode != null
                    || request.VertexIndex != null || request.MetadataKind != null || request.MetadataColumn != null
                    || request.MetadataValue != null)
                    throw new ArgumentException("A topology selection cannot include point data.");
                var available = session.Features();
                var availableIDs = available.Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
                if (featureIDs.Any(id => !availableIDs.Contains(id))) throw new ArgumentException("A selected channel topology feature no longer exists.");
                session.Select(featureIDs);
                selectedFeatureIDs = featureIDs;
                features = available;
            }
            else if (request.Operation is "undo" or "redo")
            {
                if (request.Operation == "undo" && draft.Mutations.Length > 0)
                {
                    TerrainTopologyMutation undone = draft.Mutations[^1];
                    if (undone.Operation == "importGeometry")
                    {
                        if (input is not ChannelModTerrainInput channel || undone.ImportedChannelProfiles is not { Length: > 0 })
                            throw new ArgumentException("The Channel import undo no longer matches its source profiles.");
                        undone = undone with { ImportedChannelProfiles = CaptureImportedChannelProfiles(channel.Features) };
                        channel.Clear();
                    }
                    TerrainTopologyDraft next = draft with { Mutations = draft.Mutations[..^1], RedoMutations = [.. draft.RedoMutations, undone] };
                    if (next.Mutations.Length == 0 && next.RedoMutations.Length == 0) topologyDrafts.Remove(request.InputId);
                    else topologyDrafts[request.InputId] = next;
                    session = RebuildTopology(request.InputId);
                }
                else if (request.Operation == "redo" && draft.RedoMutations.Length > 0)
                {
                    TerrainTopologyMutation redone = draft.RedoMutations[^1];
                    if (redone.Operation == "importGeometry")
                    {
                        if (input is not ChannelModTerrainInput channel || redone.ImportedChannelProfiles is not { Length: > 0 } profiles
                            || channel.Features.Count != 0)
                            throw new ArgumentException("The Channel import redo no longer matches its source profiles.");
                        RestoreImportedChannelProfiles(channel, profiles);
                    }
                    TerrainTopologyDraft next = draft with { Mutations = [.. draft.Mutations, redone], RedoMutations = draft.RedoMutations[..^1] };
                    topologyDrafts[request.InputId] = next;
                    session = RebuildTopology(request.InputId);
                }
                features = session.Features();
            }
            else
            {
                TerrainTopologyMutation mutation = RequestTopologyMutation(request, featureIDs, points);
                ValidateTopologyMutationHistory([mutation]);
                var available = session.Features().Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
                if (featureIDs.Any(id => !available.Contains(id)))
                    throw new ArgumentException("A selected channel topology feature no longer exists.");
                session.Select(featureIDs);
                bool applied = session.Apply(mutation, out string? selectedAfterMutation);
                features = session.Features();
                if (applied)
                {
                    topologyDrafts[request.InputId] = draft with { Mutations = [.. draft.Mutations, mutation], RedoMutations = [] };
                    HashSet<string> remaining = features.Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
                    if (selectedAfterMutation is not null && remaining.Contains(selectedAfterMutation))
                        selectedFeatureIDs = [selectedAfterMutation];
                }
                features = session.Features();
            }
            return TopologyData(request.InputId, features, selectedFeatureIDs);
        }
        private static TerrainTopologyMutation RequestTopologyMutation(TerrainTopologyRequest request,
            string[] featureIDs, TerrainMapCoordinate[] points)
        {
            TerrainMapCoordinate[]? mutationPoints = request.Operation is "remove" or "deleteVertex" or "maskRegion" or "setMetadata"
                ? points.Length == 0 ? null : points : points;
            return new TerrainTopologyMutation(request.Operation, featureIDs, mutationPoints, request.MapCellSize,
                request.SplitMode, request.VertexIndex, request.MetadataKind, request.MetadataColumn, request.MetadataValue);
        }
        private TerrainTopologyData TopologyData(string inputID, TerrainTopologyFeature[] features, string[] selectedFeatureIDs)
        {
            TerrainTopologyDraft draft = topologyDrafts.GetValueOrDefault(inputID) ?? new TerrainTopologyDraft([]);
            string encoded = JsonSerializer.Serialize(draft, BackendJson.Options);
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(asset + "\n" + revision + "\n" + inputID + "\n" + encoded)));
            return new TerrainTopologyData(inputID, revision, identity, features, selectedFeatureIDs,
                draft.Mutations.Length > 0, draft.RedoMutations.Length > 0);
        }
        public Dictionary<string, string> Fields()
        {
            Dictionary<string, string> result = new() { [SourceField] = selected ?? "" };
            foreach (string id in OrderedIDs())
            {
                var input = inputs[id]; result[Scope(id) + "/Name"] = input.Name;
                string? schema = Schema(input);
                if (schema == null) continue;
                result[Field(id, schema, "Name")] = input.Name; result[Field(id, schema, "TypeName")] = input.TypeName;
                Extent? extent = InputValidExtent(input);
                if (input is ConstantTerrainInput constant) result[Field(id, schema, "Value")] = N(constant.Value);
                if (input is FunctionalTerrainInput functional) result[Field(id, schema, "Expression")] = functional.Expression;
                if (extent is { } e) { result[Field(id, schema, "MinX")] = N(e.MinX); result[Field(id, schema, "MaxX")] = N(e.MaxX); result[Field(id, schema, "MinY")] = N(e.MinY); result[Field(id, schema, "MaxY")] = N(e.MaxY); }
            }
            if (selected != null && inputs.TryGetValue(selected, out ITerrainInput? selectedInput))
            {
                if (selectedInput is ChannelModTerrainInput channel) AddChannelFields(result, selected, channel);
                if (selectedInput is LeveeModTerrainInput levee) AddLeveeFields(result, selected, levee);
            }
            return result;
        }
        private string RowID(string inputID, string tableID, int index) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            asset + "\n" + revision + "\n" + inputID + "\n" + tableID + "\n" + index.ToString(CultureInfo.InvariantCulture))));
        private string ContentRowID(string inputID, string tableID, string content, int duplicateOrdinal = 0) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(asset + "\n" + revision + "\n" + inputID + "\n"
                + tableID + "\n" + content + "\n" + duplicateOrdinal.ToString(CultureInfo.InvariantCulture))));
        private static string LineIdentity(Polyline? line)
        {
            if (line == null) return "null";
            Memory<Point> points = line.GetPointBufferUnsafe();
            StringBuilder value = new();
            value.Append(points.Length.ToString(CultureInfo.InvariantCulture));
            foreach (Point point in points.Span)
                value.Append('\n').Append(point.X.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
        }
        private string[] FeatureRowIDs(string inputID, string tableID, IEnumerable<Polyline?> lines)
        {
            Dictionary<string, int> duplicates = new(StringComparer.Ordinal);
            return lines.Select(line =>
            {
                string identity = LineIdentity(line);
                int ordinal = duplicates.GetValueOrDefault(identity);
                duplicates[identity] = ordinal + 1;
                return ContentRowID(inputID, tableID, identity, ordinal);
            }).ToArray();
        }
        private string[] PropertyRowIDs(string inputID, IEnumerable<LeveeModProperties> properties)
        {
            Dictionary<string, int> duplicates = new(StringComparer.Ordinal);
            return properties.Select(property =>
            {
                string identity = string.Join("\n", property.Name ?? "", property.Mode.ToString(), N(property.TopWidth),
                    N(property.LeftWidth), N(property.RightWidth), N(property.LeftGrade), N(property.RightGrade), property.ModType.ToString());
                int ordinal = duplicates.GetValueOrDefault(identity);
                duplicates[identity] = ordinal + 1;
                return ContentRowID(inputID, LeveePropertyTable, identity, ordinal);
            }).ToArray();
        }
        private string[] ProfileRowIDs(string inputID, string tableID, string featureID,
            Geospatial.PairedData.StationElevationProfile? profile)
        {
            if (profile == null) return [];
            Dictionary<string, int> duplicates = new(StringComparer.Ordinal);
            return Enumerable.Range(0, profile.Count).Select(index =>
            {
                string identity = N(profile.Stations[index]) + "\n" + N(profile.Elevations[index]);
                int ordinal = duplicates.GetValueOrDefault(identity);
                duplicates[identity] = ordinal + 1;
                return ContentRowID(inputID, tableID, featureID + "\n" + identity, ordinal);
            }).ToArray();
        }
        private string[] RowIDsFor(string inputID, string tableID)
        {
            if (!inputs.TryGetValue(inputID, out ITerrainInput? input)) return [];
            if (input is PolygonRasterModTerrainInput && tableID == PolygonFeatureTable)
                return PolygonFeatures(inputID).Select(feature => feature.Id).ToArray();
            if (input is ChannelModTerrainInput channel && tableID == ChannelFeatureTable)
                return FeatureRowIDs(inputID, tableID, channel.Features.Select(feature => feature.Line));
            if (input is LeveeModTerrainInput levee)
            {
                if (tableID == LeveeFeatureTable) return FeatureRowIDs(inputID, tableID, levee.Features.Select(feature => feature.Line));
                if (tableID == LeveePropertyTable) return PropertyRowIDs(inputID, levee.PropertiesTable.Features);
            }
            if (input is ChannelModTerrainInput selectedChannel && tableID == ChannelProfileTable)
            {
                int[] indexes = SelectedIndexes(ChannelFeatureTable, inputID, selectedChannel.Features.Count);
                if (indexes.Length != 1) return [];
                string featureID = RowIDsFor(inputID, ChannelFeatureTable)[indexes[0]];
                return ProfileRowIDs(inputID, tableID, featureID, selectedChannel.Features[indexes[0]].Profile);
            }
            if (input is LeveeModTerrainInput selectedLevee && tableID == LeveeProfileTable)
            {
                int[] indexes = SelectedIndexes(LeveeFeatureTable, inputID, selectedLevee.Features.Count);
                if (indexes.Length != 1) return [];
                string featureID = RowIDsFor(inputID, LeveeFeatureTable)[indexes[0]];
                return ProfileRowIDs(inputID, tableID, featureID, selectedLevee.Features[indexes[0]].Profile);
            }
            if (input is ChannelModTerrainInput terrainChannel && tableID == ChannelTerrainProfileTable)
            {
                int[] indexes = SelectedIndexes(ChannelFeatureTable, inputID, terrainChannel.Features.Count);
                if (indexes.Length != 1) return [];
                Polyline? sourceLine = terrainChannel.Features[indexes[0]].Line;
                if (sourceLine == null || sourceLine.PointCount < 2) return [];
                sourceLine = sourceLine.Clone(); sourceLine.Densify(5.0);
                string featureID = RowIDsFor(inputID, ChannelFeatureTable)[indexes[0]];
                return Enumerable.Range(0, sourceLine.PointCount)
                    .Select(index => ContentRowID(inputID, tableID, featureID + "\n" + index.ToString(CultureInfo.InvariantCulture))).ToArray();
            }
            return [];
        }
        private TerrainTopologyFeature[] PolygonFeatures(string inputID)
        {
            if (!sourceVectorIndices.TryGetValue(inputID, out int sourceInputIndex) ||
                !sourcePolygonNodeIDs.TryGetValue(inputID, out string? sourceNodeID)) return [];
            TerrainPolygonTopologyEditSession topology = RebuildPolygonTopology(inputID, sourceNodeID, sourceInputIndex);
            return topology.Features(points => points);
        }
        private int[] SelectedIndexes(string tableID, string inputID, int count)
        {
            if (!selectedRows.TryGetValue(tableID, out string[]? ids)) return [];
            string[] rowIDs = RowIDsFor(inputID, tableID);
            if (rowIDs.Length != count) throw new ArgumentException("The terrain table changed while its selection was being resolved.");
            Dictionary<string, int> expected = rowIDs.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
            return ids.Select(id => expected.TryGetValue(id, out int index) ? index : throw new ArgumentException("The selected terrain table row no longer exists.")).ToArray();
        }
        private void AddChannelFields(Dictionary<string, string> fields, string inputID, ChannelModTerrainInput channel)
        {
            fields[Scope(inputID) + "/" + ChannelTinLengthField] = N(channel.TinMaxSegmentLength);
            fields[Scope(inputID) + "/" + ChannelModificationTypeField] = channel.ModificationType.ToString();
            int[] selectedFeatures = SelectedIndexes(ChannelFeatureTable, inputID, channel.Features.Count);
            if (selectedFeatures.Length == 1)
            {
                ChannelProfile profile = channel.Features[selectedFeatures[0]];
                string rowID = selectedRows[ChannelFeatureTable].Single();
                string scope = SelectionScope([rowID]);
                fields[Scoped(scope, ChannelNameField)] = profile.Name ?? "";
                fields[Scoped(scope, ChannelLineLengthField)] = N(profile.Line?.Length ?? double.NaN);
            }
        }
        private void AddLeveeFields(Dictionary<string, string> fields, string inputID, LeveeModTerrainInput levee)
        {
            int[] selectedFeatures = SelectedIndexes(LeveeFeatureTable, inputID, levee.Features.Count);
            if (selectedFeatures.Length == 1)
            {
                LeveeMod feature = levee.Features[selectedFeatures[0]];
                string rowID = selectedRows[LeveeFeatureTable].Single();
                string scope = SelectionScope([rowID]);
                fields[Scoped(scope, LeveeNameField)] = feature.Name ?? "";
                fields[Scoped(scope, LeveePropertyNameField)] = feature.LeveePropertyName ?? "";
                fields[Scoped(scope, LeveeLineLengthField)] = N(feature.Line?.Length ?? double.NaN);
            }

            LeveeModProperties[] selectedSets = selectedPropertyObjects.Where(item => levee.PropertiesTable.Features.Any(current => ReferenceEquals(current, item))).ToArray();
            string[] propertyIDs = CurrentPropertySelectionIDs(levee, inputID);
            if (selectedSets.Length == 0 || propertyIDs.Length != selectedSets.Length) return;
            string propertyScope = SelectionScope(propertyIDs);
            AddAggregate(fields, propertyScope, LeveePropertyNameFieldSet, "NameHasMismatch", selectedSets.Select(item => item.Name));
            AddAggregate(fields, propertyScope, LeveeModeField, "ModeHasMismatch", selectedSets.Select(item => item.Mode.ToString()));
            AddAggregate(fields, propertyScope, LeveeTopWidthField, "TopWidthHasMismatch", selectedSets.Select(item => FormatOptional(item.TopWidth)));
            AddAggregate(fields, propertyScope, LeveeLeftWidthField, "LeftWidthHasMismatch", selectedSets.Select(item => FormatOptional(item.LeftWidth)));
            AddAggregate(fields, propertyScope, LeveeRightWidthField, "RightWidthHasMismatch", selectedSets.Select(item => FormatOptional(item.RightWidth)));
            AddAggregate(fields, propertyScope, LeveeLeftGradeField, "LeftGradeHasMismatch", selectedSets.Select(item => FormatOptional(item.LeftGrade)));
            AddAggregate(fields, propertyScope, LeveeRightGradeField, "RightGradeHasMismatch", selectedSets.Select(item => FormatOptional(item.RightGrade)));
            AddAggregate(fields, propertyScope, LeveeModificationTypeField, "ModTypeHasMismatch", selectedSets.Select(item => item.ModType.ToString()));
        }
        private static string SelectionScope(IEnumerable<string> ids) => "TerrainSelection/" + string.Join("+", ids.Order(StringComparer.Ordinal));
        private static string Scoped(string selectionScope, string fieldID) => selectionScope + "/" + fieldID;
        private static string FormatOptional(double value) => double.IsNaN(value) ? "" : N(value);
        private static void AddAggregate(Dictionary<string, string> fields, string scope, string fieldID, string mismatchID, IEnumerable<string?> values)
        {
            string?[] items = values.ToArray();
            bool mismatch = items.Skip(1).Any(value => !string.Equals(items[0], value, StringComparison.Ordinal));
            fields[Scoped(scope, fieldID)] = mismatch ? "" : items[0] ?? "";
            fields[Scoped(scope, "LeveeModPropertiesDetailEditor/" + mismatchID)] = mismatch ? "true" : "false";
        }
        private string[] CurrentPropertySelectionIDs(LeveeModTerrainInput levee, string inputID)
        {
            if (selectedPropertyObjects.Length == 0) return [];
            string[] allIDs = PropertyRowIDs(inputID, levee.PropertiesTable.Features);
            List<string> selectedIDs = [];
            for (int index = 0; index < levee.PropertiesTable.Features.Count; index++)
                if (selectedPropertyObjects.Any(item => ReferenceEquals(item, levee.PropertiesTable.Features[index]))) selectedIDs.Add(allIDs[index]);
            selectedRows[LeveePropertyTable] = selectedIDs.ToArray();
            return selectedIDs.ToArray();
        }
        public void ApplySelections(Dictionary<string, string[]>? selections)
        {
            selectedRows.Clear();
            selectedPropertyObjects = [];
            capturedPropertyRows.Clear();
            capturedProfileRows.Clear();
            if (selections != null)
            {
                Dictionary<string, (string InputID, int Count, bool Multiple)> supported = SelectedTableSources();
                foreach (var pair in selections)
                {
                    if (!supported.TryGetValue(pair.Key, out var source))
                    {
                        if (pair.Value.Length > 0) throw new ArgumentException("Unsupported terrain table selection.");
                        continue;
                    }
                    if (pair.Value.Distinct(StringComparer.Ordinal).Count() != pair.Value.Length
                        || (!source.Multiple && pair.Value.Length > 1))
                        throw new ArgumentException("Invalid terrain table selection.");
                    string[] rowIDs = RowIDsFor(source.InputID, pair.Key);
                    if (rowIDs.Length != source.Count) throw new ArgumentException("The terrain table changed while its selection was being captured.");
                    HashSet<string> available = rowIDs.ToHashSet(StringComparer.Ordinal);
                    if (pair.Value.Any(id => !available.Contains(id))) throw new ArgumentException("The selected terrain table row no longer exists.");
                    selectedRows[pair.Key] = pair.Value.ToArray();
                    if (pair.Key == LeveePropertyTable && selected != null && inputs.TryGetValue(selected, out ITerrainInput? input)
                        && input is LeveeModTerrainInput levee)
                    {
                        Dictionary<string, LeveeModProperties> propertiesByID = rowIDs
                            .Select((id, index) => (id, property: levee.PropertiesTable.Features[index]))
                            .ToDictionary(item => item.id, item => item.property, StringComparer.Ordinal);
                        selectedPropertyObjects = pair.Value.Select(id => propertiesByID[id]).ToArray();
                        foreach (string id in pair.Value) capturedPropertyRows[id] = (levee, propertiesByID[id]);
                    }
                }
            }
            CaptureSelectedProfileRows();
            capturedTableRows = Rows();
        }
        private static string ProfileRowKey(string tableID, string rowID) => tableID + "\n" + rowID;
        private void CaptureSelectedProfileRows()
        {
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)) return;
            if (input is ChannelModTerrainInput channel)
            {
                int[] indexes = SelectedIndexes(ChannelFeatureTable, selected, channel.Features.Count);
                if (indexes.Length == 1)
                {
                    string[] ids = RowIDsFor(selected, ChannelProfileTable);
                    Geospatial.PairedData.StationElevationProfile profile = channel.Features[indexes[0]].Profile;
                    for (int index = 0; index < ids.Length; index++)
                        capturedProfileRows[ProfileRowKey(ChannelProfileTable, ids[index])] = (profile, index);
                }
            }
            if (input is LeveeModTerrainInput levee)
            {
                int[] indexes = SelectedIndexes(LeveeFeatureTable, selected, levee.Features.Count);
                if (indexes.Length == 1)
                {
                    string[] ids = RowIDsFor(selected, LeveeProfileTable);
                    Geospatial.PairedData.StationElevationProfile profile = levee.Features[indexes[0]].Profile;
                    for (int index = 0; index < ids.Length; index++)
                        capturedProfileRows[ProfileRowKey(LeveeProfileTable, ids[index])] = (profile, index);
                }
            }
        }
        public void ApplyRows(Dictionary<string, ModelRow[]>? submittedRows, Dictionary<string, ChangedCell[]>? changedCells)
        {
            if (changedCells == null) return;
            foreach (var tableChanges in changedCells)
            {
                if (tableChanges.Value == null) throw new ArgumentException("The terrain table edit is incomplete.");
                if (!capturedTableRows.TryGetValue(tableChanges.Key, out ModelRow[]? expectedRows))
                    throw new ArgumentException("This terrain table is not editable.");
                if (tableChanges.Value.Length == 0) continue;
                if (submittedRows == null || !submittedRows.TryGetValue(tableChanges.Key, out ModelRow[]? proposedRows))
                    throw new ArgumentException("The edited terrain table is missing from the request.");
                if (proposedRows.Any(row => row == null) || proposedRows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != proposedRows.Length)
                    throw new ArgumentException("The edited terrain table contains duplicate or empty rows.");
                Dictionary<string, ModelRow> proposedByID = proposedRows.ToDictionary(row => row.Id, StringComparer.Ordinal);
                Dictionary<string, int> rowIndexes = expectedRows.Select((row, index) => (row.Id, index))
                    .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
                HashSet<(string RowID, string ColumnID)> uniqueCells = [];
                foreach (ChangedCell cell in tableChanges.Value)
                {
                    if (cell == null || string.IsNullOrWhiteSpace(cell.RowID) || string.IsNullOrWhiteSpace(cell.ColumnID)
                        || !uniqueCells.Add((cell.RowID, cell.ColumnID)))
                        throw new ArgumentException("The terrain table edit contains an invalid cell identity.");
                    if (!rowIndexes.TryGetValue(cell.RowID, out int index)
                        || !proposedByID.TryGetValue(cell.RowID, out ModelRow? proposed)
                        || !proposed.Values.TryGetValue(cell.ColumnID, out string? value) || value == null)
                        throw new ArgumentException("The edited terrain table row or column no longer exists.");
                    ApplyRowCell(tableChanges.Key, expectedRows[index], cell.ColumnID, value);
                }
            }
            VerifyDependencies();
        }
        private void ApplyRowCell(string tableID, ModelRow current, string columnID, string value)
        {
            if (tableID == ChannelFeatureTable)
            {
                if (columnID != "Name") throw new ArgumentException("This channel table column is read-only or unsupported.");
                if (!TryResolveChannelFeature(current.Id, out _, out ChannelProfile feature))
                    throw new ArgumentException("The selected channel no longer exists.");
                feature.Name = value;
                return;
            }
            if (tableID == LeveeFeatureTable)
            {
                if (!TryResolveLeveeFeature(current.Id, out LeveeModTerrainInput levee, out LeveeMod feature))
                    throw new ArgumentException("The selected levee no longer exists.");
                if (columnID == "Name") { feature.Name = value; return; }
                if (columnID == "LeveePropertyName")
                {
                    if (!levee.PropertiesTable.Features.Any(property => string.Equals(property.Name, value, StringComparison.Ordinal)))
                        throw new ArgumentException("Choose a property set that belongs to this levee input.");
                    feature.LeveePropertyName = value;
                    return;
                }
                throw new ArgumentException("This levee table column is read-only or unsupported.");
            }
            if (tableID is ChannelProfileTable or LeveeProfileTable)
            {
                if (capturedProfileRows.TryGetValue(ProfileRowKey(tableID, current.Id), out var captured))
                {
                    if (captured.Index < 0 || captured.Index >= captured.Profile.Count)
                        throw new ArgumentException("The profile row no longer exists.");
                    ApplyProfileCell(captured.Profile, captured.Index, columnID, value);
                    return;
                }
                Geospatial.PairedData.StationElevationProfile profile;
                if (tableID == ChannelProfileTable)
                {
                    if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)
                        || input is not ChannelModTerrainInput channel)
                        throw new ArgumentException("The selected channel profile no longer exists.");
                    int[] indexes = SelectedIndexes(ChannelFeatureTable, selected, channel.Features.Count);
                    if (indexes.Length != 1) throw new ArgumentException("Select one channel before editing its profile.");
                    profile = channel.Features[indexes[0]].Profile;
                }
                else
                {
                    if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)
                        || input is not LeveeModTerrainInput levee)
                        throw new ArgumentException("The selected levee profile no longer exists.");
                    int[] indexes = SelectedIndexes(LeveeFeatureTable, selected, levee.Features.Count);
                    if (indexes.Length != 1) throw new ArgumentException("Select one levee before editing its profile.");
                    profile = levee.Features[indexes[0]].Profile;
                }
                string[] rowIDs = RowIDsFor(selected!, tableID);
                int profileIndex = Array.IndexOf(rowIDs, current.Id);
                if (profileIndex < 0 || profileIndex >= profile.Count)
                    throw new ArgumentException("The profile row no longer exists.");
                ApplyProfileCell(profile, profileIndex, columnID, value);
                return;
            }
            if (tableID is ChannelTerrainProfileTable or LeveePropertyTable)
                throw new ArgumentException("This terrain table is read-only.");
            throw new ArgumentException("Unknown terrain table.");
        }
        private static void ApplyProfileCell(Geospatial.PairedData.StationElevationProfile profile, int index,
            string columnID, string value)
        {
            if (columnID is not ("Station" or "Elevation"))
                throw new ArgumentException("This profile column is read-only or unsupported.");
            double parsed = Parse(value);
            Point existing = profile[index];
            profile[index] = columnID == "Station" ? new Point(parsed, existing.Y) : new Point(existing.X, parsed);
        }
        public void AddRow(string tableID)
        {
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input))
                throw new ArgumentException("Select a terrain input before adding a row.");
            if (tableID == ChannelProfileTable && input is ChannelModTerrainInput channel)
            {
                int[] indexes = SelectedIndexes(ChannelFeatureTable, selected, channel.Features.Count);
                if (indexes.Length != 1) throw new ArgumentException("Select one channel before adding a profile row.");
                channel.Features[indexes[0]].Profile.Add(0.0, 0.0);
                return;
            }
            if (tableID == LeveeProfileTable && input is LeveeModTerrainInput levee)
            {
                int[] indexes = SelectedIndexes(LeveeFeatureTable, selected, levee.Features.Count);
                if (indexes.Length != 1) throw new ArgumentException("Select one levee before adding a profile row.");
                levee.Features[indexes[0]].Profile.Add(0.0, 0.0);
                return;
            }
            if (tableID == LeveePropertyTable && input is LeveeModTerrainInput leveeInput)
            {
                HashSet<string> names = leveeInput.PropertiesTable.Features.Select(property => property.Name ?? "")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                string name = "Template";
                for (int suffix = 1; names.Contains(name); suffix++)
                    name = "Template " + suffix.ToString(CultureInfo.InvariantCulture);
                var property = new LeveeModProperties { Name = name };
                leveeInput.PropertiesTable.Add(property);
                selectedPropertyObjects = [property];
                selectedRows[LeveePropertyTable] = [PropertyRowIDs(selected, leveeInput.PropertiesTable.Features).Last()];
                return;
            }
            throw new ArgumentException("Rows cannot be added to this terrain table.");
        }
        public void CopyRows(string tableID, string[] rowIDs)
        {
            if (tableID != ChannelTerrainProfileTable || selected == null
                || !inputs.TryGetValue(selected, out ITerrainInput? input) || input is not ChannelModTerrainInput channel)
                throw new ArgumentException("Rows cannot be copied from this terrain table.");
            if (rowIDs == null || rowIDs.Length == 0 || rowIDs.Distinct(StringComparer.Ordinal).Count() != rowIDs.Length)
                throw new ArgumentException("Choose the sampled terrain profile rows to copy.");
            string[] availableIDs = RowIDsFor(selected, ChannelTerrainProfileTable);
            if (rowIDs.Length != availableIDs.Length || !rowIDs.ToHashSet(StringComparer.Ordinal).SetEquals(availableIDs))
                throw new ArgumentException("The sampled terrain profile changed before it could be copied.");
            int[] indexes = SelectedIndexes(ChannelFeatureTable, selected, channel.Features.Count);
            if (indexes.Length != 1) throw new ArgumentException("Select one channel before copying its sampled profile.");
            Polyline? sourceLine = channel.Features[indexes[0]].Line;
            if (sourceLine == null || sourceLine.PointCount < 2) throw new ArgumentException("The selected channel line cannot be sampled.");
            Polyline line = sourceLine.Clone(); line.Densify(5.0);
            double[] stations = line.CumulativePointStations();
            float[] elevations = new float[line.PointCount];
            layer.SamplePoints(layer.IndexOf(channel), line.GetPointBufferUnsafe(), elevations);
            channel.Features[indexes[0]].Profile.Clear();
            channel.Features[indexes[0]].Profile.AddRange(stations, elevations.Select(elevation => (double)elevation));
        }
        public void RemoveRows(string tableID, string[] rowIDs)
        {
            if (tableID != LeveePropertyTable || selected == null
                || !inputs.TryGetValue(selected, out ITerrainInput? input) || input is not LeveeModTerrainInput levee)
                throw new ArgumentException("Rows cannot be removed from this terrain table.");
            if (rowIDs == null || rowIDs.Length == 0 || rowIDs.Distinct(StringComparer.Ordinal).Count() != rowIDs.Length)
                throw new ArgumentException("Choose one or more levee property sets to delete.");
            if (!TryResolveLeveeProperties(rowIDs, out LeveeModTerrainInput owner, out LeveeModProperties[] removals)
                || !ReferenceEquals(owner, levee)
                || removals.Any(property => !levee.PropertiesTable.Features.Any(current => ReferenceEquals(current, property))))
                throw new ArgumentException("A selected levee property set no longer exists.");
            for (int index = levee.PropertiesTable.Features.Count - 1; index >= 0; index--)
                if (removals.Any(property => ReferenceEquals(property, levee.PropertiesTable.Features[index])))
                    levee.PropertiesTable.RemoveAt(index);
            HashSet<string> remaining = RowIDsFor(selected, LeveePropertyTable).ToHashSet(StringComparer.Ordinal);
            selectedPropertyObjects = selectedPropertyObjects.Where(item => levee.PropertiesTable.Features.Any(current => ReferenceEquals(current, item))).ToArray();
            selectedRows[LeveePropertyTable] = (selectedRows.GetValueOrDefault(LeveePropertyTable) ?? []).Where(remaining.Contains).ToArray();
        }
        private Dictionary<string, (string InputID, int Count, bool Multiple)> SelectedTableSources()
        {
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)) return [];
            if (input is ChannelModTerrainInput channel)
                return new(StringComparer.Ordinal) { [ChannelFeatureTable] = (selected, channel.Features.Count, false) };
            if (input is PolygonRasterModTerrainInput && sourceVectorIndices.ContainsKey(selected)
                && sourcePolygonNodeIDs.ContainsKey(selected))
                return new(StringComparer.Ordinal)
                {
                    [PolygonFeatureTable] = (selected, RowIDsFor(selected, PolygonFeatureTable).Length, true)
                };
            if (input is LeveeModTerrainInput levee)
                return new(StringComparer.Ordinal)
                {
                    [LeveeFeatureTable] = (selected, levee.Features.Count, false),
                    [LeveePropertyTable] = (selected, levee.PropertiesTable.Features.Count, true)
                };
            return [];
        }
        private Dictionary<string, ModelRow[]> Rows()
        {
            Dictionary<string, ModelRow[]> rows = new(StringComparer.Ordinal);
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)) return rows;
            if (input is ChannelModTerrainInput channel)
            {
                string[] featureIDs = RowIDsFor(selected, ChannelFeatureTable);
                rows[ChannelFeatureTable] = channel.Features.Select((feature, index) => new ModelRow(featureIDs[index],
                    new(StringComparer.Ordinal) { ["Name"] = feature.Name ?? "", ["LineLength"] = N(feature.Line?.Length ?? double.NaN) })).ToArray();
                int[] indexes = SelectedIndexes(ChannelFeatureTable, selected, channel.Features.Count);
                ChannelProfile? profile = indexes.Length == 1 ? channel.Features[indexes[0]] : null;
                rows[ChannelProfileTable] = ProfileRows(selected, ChannelProfileTable,
                    profile == null ? null : featureIDs[indexes[0]], profile?.Profile);
                rows[ChannelTerrainProfileTable] = ChannelTerrainRows(selected, channel, indexes.Length == 1 ? indexes[0] : null);
            }
            else if (input is PolygonRasterModTerrainInput && sourceVectorIndices.ContainsKey(selected)
                && sourcePolygonNodeIDs.ContainsKey(selected))
            {
                rows[PolygonFeatureTable] = PolygonFeatures(selected).Select(feature => new ModelRow(feature.Id,
                    feature.Metadata?.ToDictionary(field => field.Id, field => field.Value ?? "", StringComparer.Ordinal)
                        ?? new Dictionary<string, string>(StringComparer.Ordinal))).ToArray();
            }
            else if (input is LeveeModTerrainInput levee)
            {
                string[] featureIDs = RowIDsFor(selected, LeveeFeatureTable);
                string[] propertyIDs = RowIDsFor(selected, LeveePropertyTable);
                rows[LeveeFeatureTable] = levee.Features.Select((feature, index) => new ModelRow(featureIDs[index],
                    new(StringComparer.Ordinal) { ["Name"] = feature.Name ?? "", ["LeveePropertyName"] = feature.LeveePropertyName ?? "", ["LineLength"] = N(feature.Line?.Length ?? double.NaN) })).ToArray();
                rows[LeveePropertyTable] = levee.PropertiesTable.Features.Select((property, index) => new ModelRow(propertyIDs[index],
                    new(StringComparer.Ordinal) { ["Name"] = property.Name ?? "", ["Mode"] = property.Mode.ToString() })).ToArray();
                int[] indexes = SelectedIndexes(LeveeFeatureTable, selected, levee.Features.Count);
                LeveeMod? feature = indexes.Length == 1 ? levee.Features[indexes[0]] : null;
                rows[LeveeProfileTable] = ProfileRows(selected, LeveeProfileTable,
                    feature == null ? null : featureIDs[indexes[0]], feature?.Profile);
            }
            return rows;
        }
        private ModelRow[] ProfileRows(string inputID, string tableID, string? featureID,
            Geospatial.PairedData.StationElevationProfile? profile)
        {
            if (profile == null || featureID == null) return [];
            string[] ids = ProfileRowIDs(inputID, tableID, featureID, profile);
            return Enumerable.Range(0, profile.Count).Select(index => new ModelRow(ids[index],
                new(StringComparer.Ordinal) { ["Station"] = N(profile.Stations[index]), ["Elevation"] = N(profile.Elevations[index]) })).ToArray();
        }
        private ModelRow[] ChannelTerrainRows(string inputID, ChannelModTerrainInput channel, int? featureIndex)
        {
            if (featureIndex is not { } index) return [];
            Polyline? sourceLine = channel.Features[index].Line;
            if (sourceLine == null || sourceLine.PointCount < 2) return [];
            Polyline line = sourceLine.Clone();
            line.Densify(5.0);
            double[] stations = line.CumulativePointStations();
            float[] elevations = new float[line.PointCount];
            int terrainIndex = layer.IndexOf(channel);
            layer.SamplePoints(terrainIndex, line.GetPointBufferUnsafe(), elevations);
            string[] rowIDs = RowIDsFor(inputID, ChannelTerrainProfileTable);
            return Enumerable.Range(0, line.PointCount).Select(point => new ModelRow(rowIDs[point],
                new(StringComparer.Ordinal) { ["Station"] = N(stations[point]), ["Elevation"] = N(elevations[point]) })).ToArray();
        }
        private Dictionary<string, TerrainTableData> Tables()
        {
            Dictionary<string, TerrainTableData> tables = new(StringComparer.Ordinal);
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)) return tables;
            static TerrainTableColumnData C(string id, string title, string type, bool readOnly = false, double? width = null, string[]? suggestions = null) =>
                new(id, title, type, readOnly, width, suggestions);
            if (input is ChannelModTerrainInput)
            {
                tables[ChannelFeatureTable] = new([C("Name", "Name", "string", width: 220), C("LineLength", "Line Length", "double", true)] , SelectedRowIDs: CurrentSelectedRowIDs(ChannelFeatureTable));
                tables[ChannelProfileTable] = new([C("Station", "Station", "double"), C("Elevation", "Elevation", "double")], CanAddRows: true);
                tables[ChannelTerrainProfileTable] = new([C("Station", "Station", "double", true), C("Elevation", "Elevation", "double", true)], CanCopyRows: true);
            }
            else if (input is PolygonRasterModTerrainInput)
            {
                tables[PolygonFeatureTable] = new(
                    [C("ModificationType", "Modification Type", "string", true, 180),
                     C("Value", "Value", "float", true, 140)],
                    SelectedRowIDs: CurrentSelectedRowIDs(PolygonFeatureTable));
            }
            else if (input is LeveeModTerrainInput levee)
            {
                string[] suggestions = levee.PropertiesTable.Features.Select(item => item.Name ?? "").Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();
                tables[LeveeFeatureTable] = new([C("Name", "Name", "string", width: 180), C("LeveePropertyName", "Simple Cut/Fill Properties", "string", width: 230, suggestions: suggestions), C("LineLength", "Line Length", "double", true)], SelectedRowIDs: CurrentSelectedRowIDs(LeveeFeatureTable));
                tables[LeveePropertyTable] = new([C("Name", "Name", "string", true, 220), C("Mode", "Mode", "string", true, 140)],
                    CanAddRows: true, CanDeleteRows: true, SelectedRowIDs: CurrentSelectedRowIDs(LeveePropertyTable));
                tables[LeveeProfileTable] = new([C("Station", "Station", "double"), C("Elevation", "Elevation", "double")], CanAddRows: true);
            }
            foreach (var pair in requestedSelections)
                if (tables.TryGetValue(pair.Key, out TerrainTableData? table))
                    tables[pair.Key] = table with { RequestedSelectionRowIDs = pair.Value };
            return tables;
        }
        private string[] CurrentSelectedRowIDs(string tableID)
        {
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)) return [];
            if (tableID == LeveePropertyTable && input is LeveeModTerrainInput levee)
                return CurrentPropertySelectionIDs(levee, selected);
            return selectedRows.GetValueOrDefault(tableID) ?? [];
        }
        private Dictionary<string, SourceEditorChoice[]> FieldChoices()
        {
            Dictionary<string, SourceEditorChoice[]> choices = new(StringComparer.Ordinal);
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? input)) return choices;
            static SourceEditorChoice[] ModTypes() =>
            [
                new("SetValue", "Set Value"), new("TakeHigher", "Take Higher"),
                new("TakeLower", "Take Lower"), new("AddValue", "Add Value")
            ];
            if (input is ChannelModTerrainInput) choices[Scope(selected) + "/" + ChannelModificationTypeField] = ModTypes();
            if (input is LeveeModTerrainInput levee)
            {
                if (selectedRows.TryGetValue(LeveeFeatureTable, out string[]? selectedFeatures) && selectedFeatures.Length == 1)
                    choices[Scoped(SelectionScope(selectedFeatures), LeveePropertyNameField)] = levee.PropertiesTable.Features
                        .Select(property => new SourceEditorChoice(property.Name ?? "", property.Name ?? "", TitleIsUserData: true)).ToArray();
                string[] selectedProperties = CurrentPropertySelectionIDs(levee, selected);
                if (selectedProperties.Length > 0)
                {
                    string scope = SelectionScope(selectedProperties);
                    choices[Scoped(scope, LeveeModeField)] = [new("SimpleFill", "Simple Fill"), new("SimpleCut", "Simple Cut")];
                    choices[Scoped(scope, LeveeModificationTypeField)] = ModTypes();
                }
            }
            return choices;
        }
        private Dictionary<string, string[]> TextSuggestions()
        {
            if (selected != null && inputs.TryGetValue(selected, out ITerrainInput? input) && input is LeveeModTerrainInput levee)
            {
                if (selectedRows.TryGetValue(LeveeFeatureTable, out string[]? selectedFeatures) && selectedFeatures.Length == 1)
                    return new(StringComparer.Ordinal)
                    {
                        [Scoped(SelectionScope(selectedFeatures), LeveePropertyNameField)] = levee.PropertiesTable.Features.Select(property => property.Name ?? "")
                            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray()
                    };
            }
            return [];
        }
        public Dictionary<string, string[]> Groups() => OrderedIDs().Where(id => Schema(inputs[id]) != null).ToDictionary(id => Scope(id) + "/Name", id => new[] { Scope(id) + "/Name", Field(id, Schema(inputs[id])!, "Name") });
        public string[] ReadOnlyFields()
        {
            List<string> fields = OrderedIDs().Where(id => Schema(inputs[id]) != null)
                .SelectMany(id => new[] { Field(id, Schema(inputs[id])!, "Name"), Field(id, Schema(inputs[id])!, "TypeName") }).ToList();
            if (selected != null && selectedRows.TryGetValue(ChannelFeatureTable, out string[]? channelIDs) && channelIDs.Length == 1)
                fields.Add(Scoped(SelectionScope(channelIDs), ChannelLineLengthField));
            if (selected != null && selectedRows.TryGetValue(LeveeFeatureTable, out string[]? leveeIDs) && leveeIDs.Length == 1)
                fields.Add(Scoped(SelectionScope(leveeIDs), LeveeLineLengthField));
            return fields.ToArray();
        }
        public TerrainEditorData Snapshot() => new(OrderedIDs().Select(id => {
            var input = inputs[id]; string? error = input is RasterTerrainInput raster && !raster.IsValid(out string problem) ? problem : null;
            Extent? sourceExtent = InputValidExtent(input);
            TerrainInputExtent? validExtent = sourceExtent is { } extent && IsFiniteExtent(extent)
                ? new TerrainInputExtent(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY)
                : null;
            int? overviewCount = null;
            string[]? overviews = null;
            if (input is RasterTerrainInput savedRaster && error == null)
            {
                try
                {
                    using var rasterInfo = new Geospatial.GDALAssist.GDALRaster(savedRaster.Filename);
                    if (rasterInfo.GetBandCount() > 0)
                    {
                        // Keep the RasterInfoHelpers contract: index zero is the
                        // native-resolution raster, followed by each GDAL overview.
                        overviews = rasterInfo.GetPyramidLevels(1)
                            .Select(level => $"{level.Width} × {level.Height}").ToArray();
                        overviewCount = overviews.Length;
                    }
                }
                catch (Exception)
                {
                    // The saved input remains visible, but unavailable metadata is
                    // not presented as an empty or guessed overview list.
                    overviewCount = null;
                    overviews = null;
                }
            }
            int? sourceVectorIndex = sourceVectorIndices.TryGetValue(id, out int persistedIndex)
                ? persistedIndex : null;
            return new TerrainInputData(id, Scope(id), input.Name, input.TypeName, (input as RasterTerrainInput)?.Filename,
                Schema(input), error, validExtent, overviewCount, overviews, sourceVectorIndex);
        }).ToArray(), selected, importDraftToken, Rows: Rows(), Tables: Tables(), FieldChoices: FieldChoices(), TextSuggestions: TextSuggestions());
        public async Task<TerrainImportBatchJournal> AppendAsync(TerrainInputAppendRequest request, Geospatial.GDALAssist.Projection destinationProjection)
        {
            string draftToken = TerrainInputImportService.NormalizeToken(request.DraftToken, "terrain import draft");
            string batchToken = TerrainInputImportService.NormalizeToken(request.BatchToken, "terrain import batch");
            if (importDraftToken != null && importDraftToken != draftToken)
                throw new ArgumentException("The terrain import draft token does not match the current draft.");
            if (request.Files == null || request.Files.Length == 0)
                throw new ArgumentException("Choose at least one valid terrain raster.");

            VerifyDependencies();
            TerrainImportBatchJournal journal = await TerrainInputImportService.ExecuteBatchAsync(request, destinationProjection);
            if (journal.Result != null) return journal;

            // Conversion may run while a pre-existing source raster changes.
            // Keep generated outputs staged, but never extend this editor from
            // a dependency baseline that is no longer current.
            VerifyDependencies();
            importDraftToken = draftToken;
            var reservedNames = layer.Layers.Select(input => H5Reader.SanitizeObjectName(input.Name)).ToHashSet(StringComparer.Ordinal);
            string batchDirectory = Path.Combine(TerrainInputImportService.DraftDirectory(asset, draftToken), "batches", batchToken);
            Dictionary<string, string> outputNames = new(StringComparer.Ordinal);
            foreach (TerrainStagedImport stagedImport in TerrainInputImportService.StagedImports(journal))
            {
                string stagedPath = Path.Combine(batchDirectory, stagedImport.FileName);
                if (inputs.TryGetValue(stagedImport.InputID, out ITerrainInput? existingInput))
                {
                    TerrainPendingImport? existingPending = pendingImports.SingleOrDefault(item => item.Id == stagedImport.InputID);
                    if (existingPending == null || existingPending.BatchToken != batchToken
                        || existingPending.FileName != stagedImport.FileName || existingPending.Revision != stagedImport.Revision)
                        throw new ArgumentException("The terrain import output identity conflicts with this draft.");
                    reservedNames.Add(H5Reader.SanitizeObjectName(existingInput.Name));
                    outputNames[stagedImport.OperationID] = existingInput.Name;
                    continue;
                }

                string name = UniqueTerrainInputName(stagedImport.RequestedName, reservedNames);
                TerrainPendingImport pending = new(stagedImport.InputID, batchToken, stagedImport.FileName, name, stagedImport.Revision);
                RasterTerrainInput? input = null;
                bool addedToLayer = false, addedToInputs = false, addedPending = false, addedDependency = false;
                string dependencyPath = SourceFilePaths.Canonical(stagedPath);
                try
                {
                    ValidatePending(pending);
                    if (!File.Exists(stagedPath) || TerrainInputImportService.HashFileContents(stagedPath) != stagedImport.Revision)
                        throw new SourceFileChangedException();
                    if (dependencies.ContainsKey(dependencyPath))
                        throw new ArgumentException("The staged terrain input path is already in this draft.");
                    input = RasterTerrainInput.CreateAndScan(layer, stagedPath);
                    input.Name = name;
                    layer.Add(input);
                    addedToLayer = true;
                    inputs.Add(stagedImport.InputID, input);
                    addedToInputs = true;
                    pendingImports.Add(pending);
                    addedPending = true;
                    dependencies.Add(dependencyPath, stagedImport.Revision);
                    addedDependency = true;
                    outputNames[stagedImport.OperationID] = name;
                }
                catch (Exception error)
                {
                    if (addedDependency) dependencies.Remove(dependencyPath);
                    if (addedPending) pendingImports.Remove(pending);
                    if (addedToInputs) inputs.Remove(stagedImport.InputID);
                    if (addedToLayer && input != null) layer.Remove(input);
                    reservedNames.Remove(H5Reader.SanitizeObjectName(name));
                    journal = TerrainInputImportService.FailStagedImport(journal, stagedImport.OperationID, error.Message);
                }
            }

            VerifyDependencies();
            return TerrainInputImportService.UpdateAttachedOperations(journal, outputNames);
        }
        public void PublishPendingRasters(List<string> createdRasters)
        {
            if (importDraftToken == null) return;
            var pendingById = pendingImports.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (string id in OrderedIDs())
            {
                if (!pendingById.TryGetValue(id, out TerrainPendingImport? pending)) continue;
                string stagedPath = PendingPath(pending);
                if (!File.Exists(stagedPath) || TerrainInputImportService.HashFileContents(stagedPath) != pending.Revision)
                    throw new SourceFileChangedException();
                if (inputs[id] is not RasterTerrainInput raster) throw new InvalidDataException("A pending terrain import is not a raster input.");
                string destination = layer.GetAvailableRasterFilename(raster.Name);
                using (FileStream source = new(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // Track the path immediately after exclusive creation so a
                    // failed/partial copy is removed only by this save attempt.
                    createdRasters.Add(destination);
                    source.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                if (TerrainInputImportService.HashFileContents(destination) != pending.Revision)
                    throw new SourceFileChangedException();
                string stagedKey = SourceFilePaths.Canonical(stagedPath);
                string destinationKey = SourceFilePaths.Canonical(destination);
                if (!dependencies.TryGetValue(stagedKey, out string? stagedRevision) || stagedRevision != pending.Revision)
                    throw new SourceFileChangedException();
                if (dependencies.ContainsKey(destinationKey))
                    throw new IOException("The terrain raster destination is already tracked by this draft.");
                raster.RelocateSource(destination);
                dependencies.Remove(stagedKey);
                dependencies.Add(destinationKey, pending.Revision);
            }
            VerifyDependencies();
        }
        public string? CleanupCommittedPending()
        {
            if (importDraftToken == null) return null;
            string directory = TerrainInputImportService.DraftDirectory(asset, importDraftToken);
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return null;
            }
            catch (Exception error)
            {
                return error.Message;
            }
        }
        private string PendingPath(TerrainPendingImport item)
        {
            ValidatePending(item);
            string token = importDraftToken ?? throw new ArgumentException("The terrain import draft is missing its token.");
            return Path.Combine(TerrainInputImportService.DraftDirectory(asset, token), "batches", item.BatchToken, item.FileName);
        }
        private static void ValidatePending(TerrainPendingImport? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FileName) || string.IsNullOrWhiteSpace(item.Revision))
                throw new ArgumentException("The terrain import draft contains an invalid pending raster.");
            _ = TerrainInputImportService.NormalizeToken(item.BatchToken, "terrain import batch");
            string stem = Path.GetFileNameWithoutExtension(item.FileName);
            if (!string.Equals(Path.GetFileName(item.FileName), item.FileName, StringComparison.Ordinal)
                || !item.FileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(stem, "N", out _)
                || string.IsNullOrWhiteSpace(item.Id)
                || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(H5Reader.SanitizeObjectName(item.Name))
                || item.Revision.Length != 64)
                throw new ArgumentException("The terrain import draft contains an invalid pending raster.");
        }
        private static string UniqueTerrainInputName(string requestedName, HashSet<string> reservedNames)
        {
            string baseName = H5Reader.SanitizeObjectName(requestedName);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Terrain Raster";
            string candidate = baseName;
            int suffix = 2;
            while (!reservedNames.Add(H5Reader.SanitizeObjectName(candidate)))
            {
                candidate = baseName + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")";
                suffix++;
            }
            return candidate;
        }
        private bool TryChannelParentField(string fieldID, out ChannelModTerrainInput channel, out string route)
        {
            foreach (string inputID in inputs.Keys)
            {
                string prefix = Scope(inputID) + "/";
                if (!fieldID.StartsWith(prefix, StringComparison.Ordinal) || inputs[inputID] is not ChannelModTerrainInput candidate) continue;
                route = fieldID[prefix.Length..];
                if (route is ChannelTinLengthField or ChannelModificationTypeField)
                { channel = candidate; return true; }
            }
            channel = null!; route = ""; return false;
        }
        private bool TrySelectionField(string fieldID, out string[] rowIDs, out string route)
        {
            const string prefix = "TerrainSelection/";
            if (!fieldID.StartsWith(prefix, StringComparison.Ordinal)) { rowIDs = []; route = ""; return false; }
            string rest = fieldID[prefix.Length..];
            int separator = rest.IndexOf('/');
            if (separator <= 0 || separator == rest.Length - 1) { rowIDs = []; route = ""; return false; }
            rowIDs = rest[..separator].Split('+', StringSplitOptions.RemoveEmptyEntries);
            route = rest[(separator + 1)..];
            return rowIDs.Length > 0 && rowIDs.Distinct(StringComparer.Ordinal).Count() == rowIDs.Length;
        }
        private bool TryResolveChannelFeature(string rowID, out ChannelModTerrainInput channel, out ChannelProfile feature)
        {
            foreach (var pair in inputs)
            {
                if (pair.Value is not ChannelModTerrainInput candidate) continue;
                string[] ids = RowIDsFor(pair.Key, ChannelFeatureTable);
                int index = Array.IndexOf(ids, rowID);
                if (index < 0) continue;
                channel = candidate; feature = candidate.Features[index]; return true;
            }
            channel = null!; feature = null!; return false;
        }
        private bool TryResolveLeveeFeature(string rowID, out LeveeModTerrainInput levee, out LeveeMod feature)
        {
            foreach (var pair in inputs)
            {
                if (pair.Value is not LeveeModTerrainInput candidate) continue;
                string[] ids = RowIDsFor(pair.Key, LeveeFeatureTable);
                int index = Array.IndexOf(ids, rowID);
                if (index < 0) continue;
                levee = candidate; feature = candidate.Features[index]; return true;
            }
            levee = null!; feature = null!; return false;
        }
        private bool TryResolveLeveeProperties(string[] rowIDs, out LeveeModTerrainInput levee, out LeveeModProperties[] properties)
        {
            if (rowIDs.Length > 0 && rowIDs.All(capturedPropertyRows.ContainsKey))
            {
                var captured = rowIDs.Select(id => capturedPropertyRows[id]).ToArray();
                if (captured.All(item => ReferenceEquals(item.Input, captured[0].Input)
                    && item.Input.PropertiesTable.Features.Any(property => ReferenceEquals(property, item.Property))))
                {
                    levee = captured[0].Input;
                    properties = captured.Select(item => item.Property).ToArray();
                    return true;
                }
            }
            foreach (var pair in inputs)
            {
                if (pair.Value is not LeveeModTerrainInput candidate) continue;
                string[] ids = RowIDsFor(pair.Key, LeveePropertyTable);
                Dictionary<string, LeveeModProperties> byID = ids.Select((id, index) => (id, property: candidate.PropertiesTable.Features[index]))
                    .ToDictionary(item => item.id, item => item.property, StringComparer.Ordinal);
                if (rowIDs.All(byID.ContainsKey))
                {
                    levee = candidate; properties = rowIDs.Select(id => byID[id]).ToArray(); return true;
                }
            }
            levee = null!; properties = []; return false;
        }
        private void ValidateSelectionField(string key, string value)
        {
            if (!TrySelectionField(key, out string[] rowIDs, out string route))
                throw new ArgumentException("Invalid selected terrain field identity.");
            if (route.StartsWith("LeveeModPropertiesDetailEditor/", StringComparison.Ordinal))
            {
                if (!TryResolveLeveeProperties(rowIDs, out _, out LeveeModProperties[] properties))
                    throw new ArgumentException("The selected terrain property set no longer exists.");
                if (route.EndsWith("HasMismatch", StringComparison.Ordinal)) return;
                if (route == LeveePropertyNameFieldSet && string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Levee property names must not be empty.");
                if (route is LeveePropertyNameFieldSet or LeveeModeField or LeveeTopWidthField or LeveeLeftWidthField
                    or LeveeRightWidthField or LeveeLeftGradeField or LeveeRightGradeField or LeveeModificationTypeField) return;
                _ = properties;
                throw new ArgumentException("Unknown selected terrain property field.");
            }
            if (rowIDs.Length != 1) throw new ArgumentException("A selected feature editor requires exactly one feature.");
            if (route == ChannelNameField)
            {
                if (!TryResolveChannelFeature(rowIDs[0], out _, out _)) throw new ArgumentException("The selected channel no longer exists.");
                return;
            }
            if (route == ChannelLineLengthField)
            {
                if (!TryResolveChannelFeature(rowIDs[0], out _, out ChannelProfile feature)
                    || value != N(feature.Line?.Length ?? double.NaN)) throw new ArgumentException("The selected channel line length is read-only.");
                return;
            }
            if (route == LeveeNameField)
            {
                if (!TryResolveLeveeFeature(rowIDs[0], out _, out _)) throw new ArgumentException("The selected levee no longer exists.");
                return;
            }
            if (route == LeveePropertyNameField)
            {
                if (!TryResolveLeveeFeature(rowIDs[0], out _, out _)) throw new ArgumentException("The selected levee no longer exists.");
                return;
            }
            if (route == LeveeLineLengthField)
            {
                if (!TryResolveLeveeFeature(rowIDs[0], out _, out LeveeMod feature)
                    || value != N(feature.Line?.Length ?? double.NaN)) throw new ArgumentException("The selected levee line length is read-only.");
                return;
            }
            throw new ArgumentException("Unknown selected terrain feature field.");
        }
        private void ApplyChannelParentFields(Dictionary<string, string> fields)
        {
            foreach (var pair in fields)
            {
                if (!TryChannelParentField(pair.Key, out ChannelModTerrainInput channel, out string route)) continue;
                if (route == ChannelTinLengthField)
                {
                    double length = Parse(pair.Value);
                    if (length <= 0) throw new ArgumentException("TIN maximum segment length must be greater than zero.");
                    channel.TinMaxSegmentLength = length;
                }
                else if (Enum.TryParse(pair.Value, ignoreCase: false, out Geospatial.Rasters.RasterModType modificationType)
                    && Enum.IsDefined(modificationType)) channel.ModificationType = modificationType;
                else throw new ArgumentException("Invalid channel modification type.");
            }
        }
        private static double ParseLeveeValue(string value)
        {
            if (value.Length == 0 || value == "__forceReset") return double.NaN;
            bool parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double number)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            return parsed && (double.IsFinite(number) || double.IsNaN(number)) ? number : throw new ArgumentException("Levee values must be finite numbers or inherited values.");
        }
        private void ApplySelectedFields(Dictionary<string, string> fields)
        {
            foreach (var pair in fields.Where(pair => pair.Key.StartsWith("TerrainSelection/", StringComparison.Ordinal)))
            {
                if (!TrySelectionField(pair.Key, out string[] rowIDs, out string route)) continue;
                if (route.StartsWith("LeveeModPropertiesDetailEditor/", StringComparison.Ordinal))
                {
                    if (!TryResolveLeveeProperties(rowIDs, out _, out LeveeModProperties[] properties)) throw new ArgumentException("The selected terrain property set no longer exists.");
                    switch (route)
                    {
                        case LeveePropertyNameFieldSet:
                            foreach (LeveeModProperties property in properties) property.Name = pair.Value;
                            break;
                        case LeveeModeField:
                            if (!Enum.TryParse(pair.Value, ignoreCase: false, out LeveeMode mode) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid levee mode.");
                            foreach (LeveeModProperties property in properties) property.Mode = mode;
                            break;
                        case LeveeTopWidthField:
                            foreach (LeveeModProperties property in properties) property.TopWidth = ParseLeveeValue(pair.Value);
                            break;
                        case LeveeLeftWidthField:
                            foreach (LeveeModProperties property in properties) property.LeftWidth = ParseLeveeValue(pair.Value);
                            break;
                        case LeveeRightWidthField:
                            foreach (LeveeModProperties property in properties) property.RightWidth = ParseLeveeValue(pair.Value);
                            break;
                        case LeveeLeftGradeField:
                            foreach (LeveeModProperties property in properties) property.LeftGrade = ParseLeveeValue(pair.Value);
                            break;
                        case LeveeRightGradeField:
                            foreach (LeveeModProperties property in properties) property.RightGrade = ParseLeveeValue(pair.Value);
                            break;
                        case LeveeModificationTypeField:
                            if (!Enum.TryParse(pair.Value, ignoreCase: false, out Geospatial.Rasters.RasterModType modType) || !Enum.IsDefined(modType)) throw new ArgumentException("Invalid levee modification type.");
                            foreach (LeveeModProperties property in properties) property.ModType = modType;
                            break;
                        default:
                            // Mismatch flags are presentation state only. Their
                            // values are recomputed from the source properties.
                            if (!route.EndsWith("HasMismatch", StringComparison.Ordinal)) throw new ArgumentException("Unknown selected terrain property field.");
                            break;
                    }
                    continue;
                }
                if (rowIDs.Length != 1) throw new ArgumentException("A selected feature editor requires exactly one feature.");
                if (route == ChannelNameField)
                {
                    if (!TryResolveChannelFeature(rowIDs[0], out _, out ChannelProfile feature)) throw new ArgumentException("The selected channel no longer exists.");
                    feature.Name = pair.Value;
                }
                else if (route == LeveeNameField)
                {
                    if (!TryResolveLeveeFeature(rowIDs[0], out _, out LeveeMod feature)) throw new ArgumentException("The selected levee no longer exists.");
                    feature.Name = pair.Value;
                }
                else if (route == LeveePropertyNameField)
                {
                    if (!TryResolveLeveeFeature(rowIDs[0], out _, out LeveeMod feature)) throw new ArgumentException("The selected levee no longer exists.");
                    feature.LeveePropertyName = pair.Value;
                }
            }
            foreach (LeveeModTerrainInput levee in inputs.Values.OfType<LeveeModTerrainInput>())
            {
                string[] names = levee.PropertiesTable.Features.Select(property => H5Reader.SanitizeObjectName(property.Name)).ToArray();
                if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                    throw new ArgumentException("Levee property names must identify distinct property sets.");
            }
        }
        private void ForceSetLeveeProperty(TerrainLeveePropertyForceSet request)
        {
            const string changedSelection = "The selected Levee property sets changed. Select them again before applying this value.";
            string[] requestedIDs = request.RowIDs ?? [];
            string[] capturedIDs = selectedRows.GetValueOrDefault(LeveePropertyTable) ?? [];
            if (requestedIDs.Length == 0 || requestedIDs.Distinct(StringComparer.Ordinal).Count() != requestedIDs.Length
                || capturedIDs.Length != requestedIDs.Length
                || !capturedIDs.ToHashSet(StringComparer.Ordinal).SetEquals(requestedIDs))
                throw new ArgumentException(changedSelection);
            if (selected == null || !inputs.TryGetValue(selected, out ITerrainInput? selectedInput)
                || selectedInput is not LeveeModTerrainInput selectedLevee
                || !TryResolveLeveeProperties(requestedIDs, out LeveeModTerrainInput owner, out LeveeModProperties[] properties)
                || !ReferenceEquals(owner, selectedLevee) || properties.Length != requestedIDs.Length)
                throw new ArgumentException(changedSelection);

            switch (request.Command)
            {
                case "forceSetMode":
                    if (!Enum.TryParse(request.Value, ignoreCase: false, out LeveeMode mode) || !Enum.IsDefined(mode))
                        throw new ArgumentException("Invalid levee mode.");
                    foreach (LeveeModProperties property in properties) property.Mode = mode;
                    break;
                case "forceSetModType":
                    if (!Enum.TryParse(request.Value, ignoreCase: false, out Geospatial.Rasters.RasterModType modType) || !Enum.IsDefined(modType))
                        throw new ArgumentException("Invalid levee modification type.");
                    foreach (LeveeModProperties property in properties) property.ModType = modType;
                    break;
                default:
                    throw new ArgumentException("Unknown Levee ForceSet command.");
            }
        }
        public void Apply(Dictionary<string, string> fields)
        {
            var expected = Fields(); var readOnly = ReadOnlyFields().ToHashSet(StringComparer.Ordinal);
            foreach (var pair in fields.Where(x => ContainsField(x.Key)))
            {
                if (pair.Key.StartsWith("TerrainSelection/", StringComparison.Ordinal)) { ValidateSelectionField(pair.Key, pair.Value); continue; }
                if (TryChannelParentField(pair.Key, out _, out _)) continue;
                if (!expected.ContainsKey(pair.Key)) throw new ArgumentException("Unknown terrain input field.");
                if (readOnly.Contains(pair.Key) && pair.Value != expected[pair.Key] && !(pair.Key.EndsWith("/Context_Name", StringComparison.Ordinal) && fields.TryGetValue(pair.Key[..pair.Key.IndexOf('/', "TerrainInputs/".Length)] + "/Name", out string? renamed) && renamed == pair.Value)) throw new ArgumentException("This terrain input field is read-only.");
            }
            if (fields.TryGetValue(SourceField, out string? idValue))
            {
                if (idValue.Length > 0 && !OrderedIDs().Contains(idValue)) throw new ArgumentException("The selected terrain input no longer exists.");
                string? nextSelected = idValue.Length == 0 ? null : idValue;
                if (!string.Equals(selected, nextSelected, StringComparison.Ordinal))
                {
                    selectedRows.Clear();
                    selectedPropertyObjects = [];
                }
                selected = nextSelected;
            }
            foreach (string id in OrderedIDs())
            {
                var input = inputs[id]; string? schema = Schema(input);
                if (fields.TryGetValue(Scope(id) + "/Name", out string? name))
                { if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(H5Reader.SanitizeObjectName(name))) throw new ArgumentException("Terrain input names must not be empty."); input.Name = name; }
                if (schema == null) continue;
                string prefix = Scope(id) + "/" + schema + "/Context_";
                if (input is ConstantTerrainInput c && fields.TryGetValue(prefix + "Value", out string? value))
                { double n = Parse(value); if (n > float.MaxValue || n < -float.MaxValue) throw new ArgumentException("Terrain elevation is outside the source float range."); c.Value = (float)n; }
                string[] keys = ["MinX", "MaxX", "MinY", "MaxY"];
                if (keys.Any(k => fields.ContainsKey(prefix + k)))
                {
                    var numbers = keys.Select(k => Parse(fields.GetValueOrDefault(prefix + k) ?? expected[prefix + k])).ToArray();
                    if (numbers[0] >= numbers[1] || numbers[2] >= numbers[3]) throw new ArgumentException("Terrain extent maximums must exceed minimums.");
                    var extent = new Extent(numbers[0], numbers[1], numbers[2], numbers[3]);
                    if (input is ConstantTerrainInput c2) c2.ValidExtent = extent;
                    if (input is FunctionalTerrainInput f2) f2.ValidExtent = extent;
                }
                if (input is FunctionalTerrainInput f && fields.TryGetValue(prefix + "Expression", out string? expression) && expression != f.Expression)
                {
                    f.Expression = expression;
                    if (!f.IsValid(out string error)) throw new ArgumentException(error);
                }
            }
            ApplyChannelParentFields(fields);
            ApplySelectedFields(fields);
            string[] names = layer.Layers.Select(x => H5Reader.SanitizeObjectName(x.Name)).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) throw new ArgumentException("Terrain input names must identify distinct HDF groups.");
            VerifyDependencies();
        }
        private static double Parse(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : throw new ArgumentException("Terrain values must be finite numbers.");
        public void Transform(TerrainTransformRequest request)
        {
            string[] ids = OrderedIDs();
            if (request.Operation == "add")
            {
                string kind = request.InputKind ?? throw new ArgumentException("The terrain input creation kind is missing.");
                string requestedName = kind switch
                {
                    "functionalSurface" => "Functional Surface",
                    "polygonVectorMod" => "Polygon Vector Mod Layer",
                    "channelVectorMod" => "Channel Vector Mod Layer",
                    "simpleCutFill" => "Simple Cut/Fill Layer",
                    _ => throw new ArgumentException("Unknown terrain input creation kind.")
                };
                var reservedNames = layer.Layers.Select(input => H5Reader.SanitizeObjectName(input.Name)).ToHashSet(StringComparer.Ordinal);
                string name = UniqueTerrainInputName(requestedName, reservedNames);
                string id = Guid.NewGuid().ToString("N");
                ITerrainInput input = CreateInput(kind, name, request.HintExtent);
                layer.Add(input);
                inputs.Add(id, input);
                pendingInputs.Add(new TerrainPendingInput(id, kind, name, request.HintExtent));
                selected = id;
            }
            else if (request.Operation == "reorder")
            {
                if (request.From is not { } from || request.To is not { } to || from < 0 || to < 0 || from >= ids.Length || to >= ids.Length) throw new ArgumentException("Invalid terrain input order.");
                layer.Reorder(from, to);
            }
            else if (request.Operation == "remove")
            {
                if (request.ItemId == null || !ids.Contains(request.ItemId)) throw new ArgumentException("The selected terrain input no longer exists.");
                ITerrainInput input = inputs[request.ItemId];
                layer.Remove(input);
                inputs.Remove(request.ItemId);
                topologyBaselines.Remove(request.ItemId);
                topologyDrafts.Remove(request.ItemId);
                sourceVectorIndices.Remove(request.ItemId);
                sourcePolygonNodeIDs.Remove(request.ItemId);
                polygonBaselines.Remove(request.ItemId);
                polygonTopologyDrafts.Remove(request.ItemId);
                polygonTopologySessions.Remove(request.ItemId);
                polygonSettings.Remove(request.ItemId);
                channelGeometryPayloads.Remove(request.ItemId);
                pendingInputs.RemoveAll(item => item.Id == request.ItemId);
                if (input is IDisposable disposable) disposable.Dispose();
                if (selected == request.ItemId) selected = null;
            }
            else if (request.Operation == "selectInput")
                CaptureSelectedInput(new(StringComparer.Ordinal) { [SourceField] = request.ItemId ?? "" });
            else if (request.Operation == "addRow")
                AddRow(request.TableId ?? throw new ArgumentException("The terrain table is missing."));
            else if (request.Operation == "copyRows")
                CopyRows(request.TableId ?? throw new ArgumentException("The terrain table is missing."), request.RowIDs ?? []);
            else if (request.Operation == "removeRows")
                RemoveRows(request.TableId ?? throw new ArgumentException("The terrain table is missing."), request.RowIDs ?? []);
            else if (request.Operation == "forceSetLeveeProperty")
                ForceSetLeveeProperty(request.LeveeForceSet ?? throw new ArgumentException("The Levee ForceSet request is missing."));
            else if (request.Operation == "selectRows")
            {
                string tableID = request.TableId ?? throw new ArgumentException("The terrain table is missing.");
                string[] requested = request.RowIDs ?? [];
                string[] normalized = requested;
                if (selected != null && inputs.TryGetValue(selected, out ITerrainInput? input) && input is LeveeModTerrainInput levee)
                {
                    // Fields were applied to captured objects before this navigation.
                    // Rebase only their exact prior IDs, never names or positions.
                    LeveeModProperties[] properties = levee.PropertiesTable.Features.ToArray();
                    string[] currentIDs = PropertyRowIDs(selected, properties);
                    Dictionary<string, string> aliases = new(StringComparer.Ordinal);
                    foreach (var pair in capturedPropertyRows)
                    {
                        if (!ReferenceEquals(pair.Value.Input, levee)) continue;
                        int index = Array.FindIndex(properties, property => ReferenceEquals(property, pair.Value.Property));
                        if (index >= 0) aliases[pair.Key] = currentIDs[index];
                    }
                    _ = CurrentPropertySelectionIDs(levee, selected);
                    if (tableID == LeveePropertyTable)
                        normalized = requested.Select(id => aliases.GetValueOrDefault(id, id)).ToArray();
                }
                Dictionary<string, string[]> selections = new(selectedRows, StringComparer.Ordinal) { [tableID] = normalized };
                ApplySelections(selections);
                requestedSelections[tableID] = requested;
            }
            else throw new ArgumentException("Unknown terrain input operation.");
            VerifyDependencies();
        }
        public TerrainMapData Preview(string? inputID)
        {
            var selectedInput = inputID == null ? null : OrderedIDs().Contains(inputID) ? inputs[inputID] : throw new ArgumentException("The selected terrain input no longer exists.");
            foreach (var input in layer.Layers) if (!input.IsValid(out string problem)) throw new ArgumentException(problem);
            SpatialGridPreview? baseRaster = SamplePreview(layer, selectedInput: false);
            SpatialGridPreview? selectedRaster = selectedInput == null ? null
                : SamplePreview(selectedInput.GetResampler() ?? throw new ArgumentException("The terrain input cannot be sampled."), selectedInput: true);
            TerrainMapFeature[] features = selectedInput switch
            {
                ChannelModTerrainInput channel => MapFeatures(inputID!, ChannelFeatureTable,
                    channel.Features.Select(feature => feature.Line).ToArray()),
                LeveeModTerrainInput levee => MapFeatures(inputID!, LeveeFeatureTable,
                    levee.Features.Select(feature => feature.Line).ToArray()),
                _ => []
            };
            return new TerrainMapData(baseRaster, selectedRaster, inputID, revision, features);
        }
        private TerrainMapFeature[] MapFeatures(string inputID, string tableID, Polyline?[] lines)
        {
            string[] rowIDs = FeatureRowIDs(inputID, tableID, lines);
            List<TerrainMapFeature> mapped = new(lines.Length);
            for (int index = 0; index < lines.Length; index++)
            {
                Polyline? line = lines[index];
                if (line == null || line.Count < 2) continue;
                Memory<Point> sourcePoints = line.GetPointBufferUnsafe();
                if (sourcePoints.Span.ToArray().Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) continue;
                mapped.Add(new TerrainMapFeature(rowIDs[index], tableID, sourcePoints.Span.ToArray()
                    .Select(point => new TerrainMapCoordinate(point.X, point.Y)).ToArray()));
            }
            return mapped.ToArray();
        }
        private static SpatialGridPreview? SamplePreview(IResample<float> source, bool selectedInput)
        {
            var e = source.ValidExtent;
            if (!double.IsFinite(e.MinX) || !double.IsFinite(e.MaxX) || !double.IsFinite(e.MinY) || !double.IsFinite(e.MaxY) || e.MinX >= e.MaxX || e.MinY >= e.MaxY) return null;
            const int width = 256, height = 256; var points = new Point[width * height]; var values = new float[points.Length];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) points[y * width + x] = new(e.MinX + (x + .5) * (e.MaxX - e.MinX) / width, e.MaxY - (y + .5) * (e.MaxY - e.MinY) / height);
            source.SamplePoints(points, values);
            bool Valid(float v) => float.IsFinite(v) && (!source.NoDataValue.HasValue || v != source.NoDataValue.Value);
            var valid = values.Where(Valid).ToArray(); double? min = valid.Length == 0 ? null : valid.Min(), max = valid.Length == 0 ? null : valid.Max(); byte[] rgba = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++) if (Valid(values[i]))
            {
                double t = max > min ? (values[i] - min!.Value) / (max!.Value - min.Value) : .5;
                if (selectedInput)
                {
                    rgba[i * 4] = (byte)(24 + 50 * t); rgba[i * 4 + 1] = (byte)(105 + 85 * t);
                    rgba[i * 4 + 2] = 245; rgba[i * 4 + 3] = 160;
                }
                else
                {
                    byte gray = (byte)(90 + 105 * t);
                    rgba[i * 4] = gray; rgba[i * 4 + 1] = gray; rgba[i * 4 + 2] = gray; rgba[i * 4 + 3] = 255;
                }
            }
            return new(width, height, rgba, e.MinX, e.MinY, e.MaxX, e.MaxY, min, max);
        }
    }
}
