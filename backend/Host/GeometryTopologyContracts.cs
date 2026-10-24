namespace Ras.Backend;

public sealed record GeometryTopologyModelRequest(string AssetPath, string Kind, string? ProjectPath,
    string? ProjectXml = null, CoreLayerRequestContext? CoreLayerContext = null);
public sealed record GeometryTopologyLoadRequest(GeometryTopologyModelRequest Model, string? Revision = null);
public sealed record GeometryTopologyEditRequest(GeometryTopologyModelRequest Model, string Revision, string DraftState,
    GeometryTopologyMutation Mutation, GeometryTopologyMapRequestContext? MapRequestContext = null);
public sealed record GeometryTopologySaveRequest(GeometryTopologyModelRequest Model, string Revision, string DraftState);

/// <summary>
/// Transient owner capture for a single map interaction. This is echoed by the
/// response for client-side currentness checks, but is never journaled in the
/// replayable topology mutation.
/// </summary>
public sealed record GeometryTopologyMapRequestContext(string RequestID, string MapID, string ContextToken,
    string GeometryRevision, string ExpectedDraftIdentity);

public sealed record GeometryTopologySpatialSelectRequest(string RequestID, GeometryTopologyModelRequest Model,
    string Revision, string DraftState, string DraftIdentity, string MapID, string ContextToken,
    string GeometryRevision, string SelectionScope, string SelectionMode, string Gesture,
    GeometryTopologyCoordinate[] WorldPoints, double MapCellSize, string? MarqueeHitMode,
    int[] CurrentArcFIDs, int[] CurrentNodeFIDs, int[] CurrentRegionFIDs);

public sealed record GeometryTopologySelectedFeature(string FeatureKind, int FID, string TableRowID,
    string[] MapFeatureIDs);

public sealed record GeometryTopologySpatialSelectResult(string RequestID, string MapID, string ContextToken,
    string GeometryRevision, string Revision, string DraftIdentity,
    GeometryTopologySelectedFeature[] Selections);

/// <summary>
/// A source-backed hit on one exact arc segment for starting an Add, SmartAdd,
/// or DeepConnect arc. Its point must equal the captured first anchor and the
/// accepted path's first coordinate; map/session ownership remains in the
/// transient GeometryTopologyMapRequestContext envelope.
/// </summary>
public sealed record GeometryTopologyArcSnapTarget(int ArcFID, int SourceSegmentIndex,
    GeometryTopologyCoordinate Point);

/// <summary>
/// A deterministic source operation appended to the existing Geometry topology
/// draft journal. Coordinates and stable source FIDs are persisted; map/session
/// tokens remain in the request envelope only.
/// </summary>
public sealed record GeometryTopologyMapCommand(string Action, string FeatureKind, int[] FeatureIDs,
    int? NodeFID = null, int? ArcFID = null, int? PointIndex = null,
    int? SourceSegmentIndex = null, double? SourceSegmentFraction = null,
    GeometryTopologyCoordinate[]? Coordinates = null, double MapCellSize = 0, double? Epsilon = null,
    int? SmoothWindowSize = null, int? SmoothIterations = null, string? SplitMode = null,
    GeometryTopologyDeepConnectSource? DeepConnectSource = null,
    GeometryTopologySmartAddSource? SmartAddSource = null)
{
    /// <summary>Exact captured source arc hit used only when an Add begins on an arc.</summary>
    public GeometryTopologyArcSnapTarget? StartArcTarget { get; init; }

    /// <summary>
    /// Exact current source node selected by the map at the first Add,
    /// SmartAdd, or DeepConnect point. The point is validated against the
    /// source six-cell hit window before this stable FID overrides source
    /// first-node hit testing.
    /// </summary>
    public int? StartNodeFID { get; init; }

    /// <summary>
    /// Exact node selection captured when a node drag starts. NodeFID is the
    /// dragged node and must belong to this set; an unselected drag sends its
    /// single hit node, matching MoveNodeHandler's selection behavior.
    /// </summary>
    public int[] NodeFIDs { get; init; } = [];
}

/// <summary>
/// Exact, current Geometry-to-Terrain association used by SmartAdd. SourceRevision
/// hashes the Geometry, owning project/context, terrain HDF, and every raster
/// input that can contribute to Terrain.Resample.
/// </summary>
public sealed record GeometryTopologySmartAddTerrainCapture(string GeometryAssetPath,
    string GeometryRevision, string ProjectPath, string? ProjectSessionID,
    string? CoreLayerContextIdentityToken, string CanonicalTerrainPath, string TerrainName,
    string SourceRevision);

/// <summary>The actual camera raster used by the map event that requested SmartAdd.</summary>
public sealed record GeometryTopologySmartAddWindow(GeometryTopologyExtent Extent, int Rows, int Columns);

/// <summary>Retained options from SmartAppendLineFeaturePointHandler.</summary>
public sealed record GeometryTopologySmartAddOptions(string Algorithm = "HessianRidgeness",
    double DpTolerancePixels = 1.5, double MaxSegmentPixels = 1500.0,
    double PreviewThrottlePixels = 1.5, double HighGroundAlpha = 3.0,
    double HighGroundBailRatio = 5.0, int HighGroundSnapRadiusCells = 15,
    double HessianFrangiBeta = 0.5, double HessianFeaturePenaltyKappa = 3.0,
    double HessianOffFeatureBailMultiplier = 100.0, int HessianMinExpansionsBudget = 250000,
    int HessianExpansionsPerEuclidPx = 500, int HessianMaxEuclidCells = 1500);

/// <summary>
/// One source SmartAdd segment. The terrain capture is echoed from the current
/// topology snapshot; the window is captured from the exact current map event.
/// </summary>
public sealed record GeometryTopologySmartAddSource(GeometryTopologySmartAddTerrainCapture TerrainCapture,
    GeometryTopologySmartAddWindow Window, GeometryTopologySmartAddOptions? Options = null);

/// <summary>
/// The accepted source line captured by the map owner for a DeepConnect edit.
/// Its locator/revision/FID identify provenance; points plus arc-relative
/// segment/fraction anchors make journal replay deterministic. FeatureFID is
/// the retained renderer's line-reader ordinal for GeoJSON polylines and all
/// polygon rings; only persisted Shapefile polylines keep record-ordinal plus
/// source-part PathIndex semantics.
/// </summary>
public sealed record GeometryTopologyDeepConnectSource(string SourceKind, string SourceLocator,
    string SourceRevision, int FeatureFID, int PathIndex, string ComponentIdentity,
    GeometryTopologyCoordinate[] Points, int StartSegmentIndex, double StartSegmentFraction,
    int EndSegmentIndex, double EndSegmentFraction,
    GeometryTopologyDeepConnectVectorSource? VectorSource = null,
    GeometryTopologyDeepConnectTopologySource? TopologySource = null);

/// <summary>
/// Persisted map owner and source layer identity for a vector snap target. The
/// Host resolves the layer again and reads its exact source path; the captured
/// feature coordinates are only a consistency check, never the edit source.
/// </summary>
public sealed record GeometryTopologyDeepConnectVectorSource(MapTreeRequest Map,
    CatalogTreeNodeLocator Node, MapSourceFamilyCapture? ExpectedFamily);

/// <summary>
/// Exact Geometry draft owner for a Conceptual Mesh arc snap target. This is a
/// transient read capture and is not copied into the replayable map command.
/// </summary>
public sealed record GeometryTopologyDeepConnectTopologySource(
    GeometryTopologyModelRequest Model, string Revision, string DraftState,
    string DraftIdentity, GeometryTopologyMapRequestContext MapRequestContext,
    string FeatureKind, string FeatureID);

/// <summary>
/// A validated source mutation retained as part of an unsaved topology draft.
/// Arc choice Force Set uses operation <c>forceSetArcChoice</c>, feature kind
/// <c>arc</c>, exact selected source FIDs in <see cref="FeatureIDs"/>, and
/// <see cref="FieldID"/>/<see cref="Value"/> for either ConstrainStretching
/// or LaplaceBC. Arc Bias Clear uses <c>forceResetArcBias</c> with exact FIDs
/// and field <c>Bias</c>; its source value is NaN, matching the retained UI.
/// Both operations remain draft mutations until the parent geometry save.
/// </summary>
public sealed record GeometryTopologyMutation(string Operation, string FeatureKind, int[] FeatureIDs,
    string? FieldID = null, string? Value = null, string? TagName = null, string? TagType = null, string? TagValue = null)
{
    // The retained global factor tool can target both source collections in one
    // command. These fields remain optional so older topology draft payloads
    // keep their existing wire shape and replay behavior.
    public bool ApplyToSelectedOnly { get; init; }
    public string? FactorText { get; init; }
    public int[] ArcFeatureIDs { get; init; } = [];
    public int[] ControlPointFeatureIDs { get; init; } = [];
    public int[] NodeFeatureIDs { get; init; } = [];

    // Recipe text is intentionally opaque. The source setter preserves invalid
    // raw text; validation is returned by the read-only recipe query.
    public string? RecipeText { get; init; }
    public bool ForceSet { get; init; }

    // Arc-point rows are source identities, not rendered polyline positions.
    // PointKind is exactly "start", "internal", or "end". Internal indices
    // are zero-based; endpoint indices are source topology node indices.
    public string? PointKind { get; init; }
    public int? InternalPointIndex { get; init; }
    public int? EndpointNodeIndex { get; init; }
    public string? Axis { get; init; }
    public string? RawValue { get; init; }

    // Source-backed map interaction commands remain inside the ordinary
    // topology journal so undo/redo and the parent's eventual Geometry save
    // use the existing draft lifecycle.
    public GeometryTopologyMapCommand? MapCommand { get; init; }

    // Shapefile line/polygon import is an ordinary parent geometry draft edit.
    // The source family is retained for provenance; projected feature points
    // make draft replay independent of the external files after acceptance.
    public GeometryTopologyImportPayload? ImportPayload { get; init; }
}

public sealed record GeometryTopologyImportPayload(
    string Mode,
    bool MergeContiguousArcs,
    bool RemoveExternalArcs,
    GeometryTopologyImportSource[] Sources,
    GeometryTopologyImportFeature[] Features);

public sealed record GeometryTopologyImportSource(string Path, MapSourceFamilyCapture Family);
public sealed record GeometryTopologyImportFeature(string GeometryType, GeometryTopologyCoordinate[] Points);

public sealed record GeometryVectorImportInspectRequest(GeometryTopologyModelRequest Model, string Revision,
    string DraftState, string DraftIdentity, string[] Paths);
public sealed record GeometryVectorImportApplyRequest(GeometryTopologyModelRequest Model, string Revision,
    string DraftState, string DraftIdentity, GeometryVectorImportSelectedSource[] Sources,
    string Mode, bool MergeContiguousArcs, bool RemoveExternalArcs);
public sealed record GeometryVectorImportSelectedSource(string Path, MapSourceFamilyCapture Family);
public sealed record GeometryVectorImportInspection(string Revision, string DraftIdentity,
    GeometryVectorImportSource[] Sources, GeometryVectorImportIssue[] Issues);
public sealed record GeometryVectorImportSource(string Path, string Name, string ShapeType, int ShapeTypeCode,
    int FeatureCount, MapSourceFamilyCapture Family, GeometryVectorImportFeature[] Features);
public sealed record GeometryVectorImportFeature(int SourceOrdinal, string GeometryType,
    GeometryTopologyCoordinate[] Points);
public sealed record GeometryVectorImportIssue(string Path, string Message);

public sealed record GeometryTopologyArcPointsRequest(GeometryTopologyModelRequest Model, string Revision,
    string DraftState, int ArcFID);
public sealed record GeometryTopologyArcPointsSnapshot(string Revision, string DraftIdentity, int ArcFID,
    GeometryTopologyArcPoint[] Rows);
public sealed record GeometryTopologyArcPoint(int RowIndex, string PointKind, int? InternalPointIndex,
    int? EndpointNodeIndex, double X, double Y);

public sealed record GeometryTopologyRecipeRequest(GeometryTopologyModelRequest Model, string Revision,
    string DraftState, int[] ControlPointFIDs, string? RecipeText = null);
public sealed record GeometryTopologyRecipeSnapshot(string Revision, string DraftIdentity,
    int[] ControlPointFIDs, bool HasSelection, bool HasMismatch, string? CommonRecipe,
    string RawText, bool IsValid, string? ParseIssue, GeometryTopologyRecipeStep[] Steps,
    GeometryTopologyRecipeStepSchema[] StepSchema, GeometryTopologyControlPointRecipe[] SelectedRecipes,
    string[]? SelectedPatchTypes = null);
public sealed record GeometryTopologyControlPointRecipe(int FID, string Recipe);
public sealed record GeometryTopologyRecipeStep(string Name, Dictionary<string, string> Parameters);
public sealed record GeometryTopologyRecipeStepSchema(string Name, string[] Aliases,
    GeometryTopologyRecipeParameter[] Parameters);
public sealed record GeometryTopologyRecipeParameter(string Name, string SourceType, string DefaultValue);

public sealed record GeometryTopologyFilterRequest(GeometryTopologyModelRequest Model, string Revision,
    string DraftState, string FeatureKind, GeometryTopologyFilterClause[] Clauses);
public sealed record GeometryTopologyFilterClause(string Type, string? TagDefinitionID,
    string? PropertyColumnID, string Operator, string? Value = null);
public sealed record GeometryTopologyFilterSnapshot(string Revision, string DraftIdentity, string FeatureKind,
    int[] MatchedFeatureIDs, GeometryTopologyPropertyDescriptor[] PropertyDescriptors,
    GeometryTopologyTagDefinition[] TagDefinitions);
public sealed record GeometryTopologyPropertyDescriptor(string Name, string ColumnName, string SourceType,
    bool IsEnum = false);

public sealed record GeometryTopologySnapshot(string Revision, string DraftState, bool CanEdit, bool IsDirty,
    GeometryTopologyFeatureTable Arcs, GeometryTopologyFeatureTable Regions, GeometryTopologyFeatureTable Nodes,
    GeometryTopologyTagDefinition[] ArcTags, GeometryTopologyTagDefinition[] RegionTags,
    GeometryTopologyRecentTag[] RecentArcTags, GeometryTopologyRecentTag[] RecentRegionTags,
    GeometryTopologyMap? Map = null, GeometryTopologyCompletionResult? Completion = null,
    bool CanUndo = false, bool CanRedo = false)
{
    public GeometryTopologyMapRequestContext? MapRequestContext { get; init; }
    public GeometryTopologySmartAddTerrainCapture? SmartAddTerrainCapture { get; init; }
}

/// <summary>
/// The real, ephemeral result from retained conceptual-mesh topology completion.
/// It is not written into the geometry HDF. SourceRevision identifies the saved
/// baseline; DraftIdentity identifies the exact opaque DraftState returned with it.
/// </summary>
public sealed record GeometryTopologyCompletionResult(string SourceRevision, string DraftIdentity,
    bool IsComplete, GeometryTopologyCompletionMessage[] Messages);
public sealed record GeometryTopologyCompletionMessage(string ID, string Code, string Severity,
    string? RegionCompletionSeverity, string Text, GeometryTopologyLogValue[] LogData,
    GeometryTopologyDiagnosticTarget[] Targets);
public sealed record GeometryTopologyLogValue(string Key, string Value, string ValueType,
    int? IntegerValue = null);
/// <summary>A safely resolved source feature and the exact map feature IDs it represents.</summary>
public sealed record GeometryTopologyDiagnosticTarget(string FeatureKind, int FID,
    string TableRowID, string[] MapFeatureIDs);

public sealed record GeometryTopologyFeatureTable(string FeatureKind, GeometryTopologyColumn[] Columns,
    GeometryTopologyField[] EditableFields, GeometryTopologyFeatureRow[] Rows);
public sealed record GeometryTopologyColumn(string ID, string Title, string SourceType, bool ReadOnly = true,
    bool IsTag = false, bool IsUserDefined = false);
public sealed record GeometryTopologyField(string ID, string Title, string SourceType, string[] Choices,
    bool ReadOnly = false);
public sealed record GeometryTopologyFeatureRow(string ID, int FID, Dictionary<string, string> Values,
    double? X = null, double? Y = null, int? StartNodeID = null, int? EndNodeID = null);
public sealed record GeometryTopologyCoordinate(double X, double Y);
public sealed record GeometryTopologyExtent(double MinX, double MinY, double MaxX, double MaxY);
public sealed record GeometryTopologyMapFeature(string ID, string FeatureKind, int? FID, string GeometryType,
    string[] SelectionIDs, GeometryTopologyCoordinate[][] Paths, GeometryTopologyIncidentArc[]? IncidentArcs = null);
/// <summary>
/// A source NodeArcConnection in source order. Length is the real topology
/// polyline length for ArcID, not a rendered or inferred screen measurement.
/// </summary>
public sealed record GeometryTopologyIncidentArc(int ArcID, double Length);
public sealed record GeometryTopologyMap(GeometryTopologyExtent? Extent, GeometryTopologyMapFeature[] Features);
public sealed record GeometryTopologyTagDefinition(string ID, string Type);
public sealed record GeometryTopologyRecentTag(string Name, string Type, string Value);

/// <summary>
/// JSON is passed back as an opaque string because the backend protocol launches
/// one process per request. Mutations stay in memory on the client until Save.
/// </summary>
public sealed record GeometryTopologyDraftState(string AssetPath, string Revision,
    GeometryTopologyMutation[] Mutations, GeometryTopologyRecentTag[] RecentArcTags, GeometryTopologyRecentTag[] RecentRegionTags)
{
    /// <summary>Commands undone most recently are kept in pop order at the array tail.</summary>
    public GeometryTopologyMutation[] RedoMutations { get; init; } = [];
}
