namespace Ras.Backend;

public sealed record GeometryCulvertModelRequest(string AssetPath, string Kind, string? ProjectPath,
    string? ProjectXml = null, CoreLayerRequestContext? CoreLayerContext = null);

public sealed record GeometryCulvertLoadRequest(GeometryCulvertModelRequest Model, string? Revision = null,
    string? DraftState = null, string[]? SelectedBarrelIDs = null);

public sealed record GeometryCulvertEditRequest(GeometryCulvertModelRequest Model, string Revision,
    string DraftState, GeometryCulvertMutation Mutation, string[]? SelectedBarrelIDs = null);

/// <summary>A typed edit against source row identities tied to one HDF revision.</summary>
public sealed record GeometryCulvertMutation(string Operation, string FeatureKind, string[] FeatureIDs,
    string? FieldID = null, string? Value = null, string? DraftID = null,
    GeometryCulvertCoordinate[]? Polyline = null,
    double? DeltaX = null, double? DeltaY = null,
    GeometryCulvertCopiedBarrel[]? CopiedBarrels = null,
    double? Tolerance = null, int? WindowSize = null, int? Passes = null,
    string? PreviewID = null, string? TargetFeatureID = null,
    GeometryCulvertCoordinate? SplitPoint = null,
    string[]? OutputDraftIDs = null, string? SplitMode = null, double? MapCellSize = null,
    double? MergeTolerance = null, double? OffsetDistance = null, string? OffsetSides = null,
    bool? IncludeEndpoints = null, bool? KeepOriginal = null);

/// <summary>
/// A side-effect-free preview of one source Geometry Culverts polyline tool.
/// PreviewID is caller correlation for stale UI replies; revision, draft state
/// and captured feature identities are the actual applicability guards.
/// </summary>
public sealed record GeometryCulvertMapCommandPreviewRequest(
    GeometryCulvertModelRequest Model, string Revision, string DraftState,
    string PreviewID, string Operation, string[] FeatureIDs,
    GeometryCulvertCoordinate? SplitPoint = null, string? SplitMode = null,
    double? MapCellSize = null, double? MergeTolerance = null,
    double? OffsetDistance = null, string? OffsetSides = null,
    bool IncludeEndpoints = false, bool KeepOriginal = false,
    string[]? OutputDraftIDs = null, string? TargetFeatureID = null);

public sealed record GeometryCulvertMapCommandOutput(string DraftID,
    GeometryCulvertCoordinate[] Polyline);

public sealed record GeometryCulvertMapCommandPreviewResponse(
    string PreviewID, string Revision, string DraftState, string DependencyRevision,
    string Operation, string[] CapturedFeatureIDs, string[] RemovedFeatureIDs,
    string[] RetainedFeatureIDs, GeometryCulvertMapCommandOutput[] Outputs,
    bool HasChange, bool CanApply, string? WarningCode = null, string? ErrorCode = null);

/// <summary>The exact map display raster window used by HitTestArgs.Window.</summary>
public sealed record GeometryCulvertSmartAddWindow(GeometryCulvertExtent Extent,
    int Rows, int Columns);

/// <summary>Retained SmartAdd handler settings and defaults.</summary>
public sealed record GeometryCulvertSmartAddOptions(
    string Algorithm = "HessianRidgeness", double DpTolerancePixels = 1.5,
    double MaxSegmentPixels = 1500.0, double PreviewThrottlePixels = 1.5,
    double HighGroundAlpha = 3.0, double HighGroundBailRatio = 5.0,
    int HighGroundSnapRadiusCells = 15, double HessianFrangiBeta = 0.5,
    double HessianFeaturePenaltyKappa = 3.0,
    double HessianOffFeatureBailMultiplier = 100.0,
    int HessianMinExpansionsBudget = 250000,
    int HessianExpansionsPerEuclidPx = 500, int HessianMaxEuclidCells = 1500);

public sealed record GeometryCulvertSmartAddPreviewRequest(
    GeometryCulvertModelRequest Model, string Revision, string DraftState,
    string PreviewID, GeometryCulvertCoordinate Start, GeometryCulvertCoordinate End,
    GeometryCulvertSmartAddWindow Window,
    GeometryCulvertSmartAddOptions? Options = null);

public sealed record GeometryCulvertSmartAddPreviewResponse(
    string PreviewID, string Revision, string DraftState, string DependencyRevision,
    string? TerrainName, bool CanAppend, GeometryCulvertCoordinate[] Path,
    string? ErrorCode = null);

public sealed record GeometryCulvertSnapshot(string Revision, string DraftState, bool CanEdit, bool IsDirty,
    GeometryCulvertFeatureTable Barrels, GeometryCulvertFeatureTable BarrelProperties,
    GeometryCulvertFeatureTable OpeningProperties, string? AssociatedTerrainName,
    GeometryCulvertProfile[] Profiles, GeometryCulvertExtent? AssociatedTerrainExtent = null,
    string? DependencyRevision = null);

/// <summary>
/// Clipboard payload matching the fields copied by source CulvertBarrel.Clone.
/// Numeric values use invariant round-trip strings so source NaN sentinels are
/// preserved through the JSON ledger.
/// </summary>
public sealed record GeometryCulvertCopiedBarrel(string DraftID, string Name,
    string BarrelPropertyName, string UpstreamInvert, string DownstreamInvert,
    string UpstreamOpeningName, string DownstreamOpeningName,
    GeometryCulvertCoordinate[] Polyline);

public sealed record GeometryCulvertTerrainPreviewRequest(GeometryCulvertModelRequest Model,
    string? ExpectedDependencyRevision = null);

/// <summary>Pixels from the retained RasterRenderLayer terrain renderer.</summary>
public sealed record GeometryCulvertTerrainPreview(string DependencyRevision,
    string? TerrainName, GeometryCulvertExtent? SourceExtent,
    GeometryCulvertExtent? RenderedExtent, int Width, int Height, byte[] Rgba);

public sealed record GeometryCulvertFeatureTable(string FeatureKind, GeometryCulvertColumn[] Columns,
    GeometryCulvertField[] EditableFields, GeometryCulvertFeatureRow[] Rows);

public sealed record GeometryCulvertColumn(string ID, string Title, string SourceType, bool ReadOnly = true);

public sealed record GeometryCulvertField(string ID, string Title, string SourceType, string[] Choices,
    bool ReadOnly = false, double? Minimum = null, double? Maximum = null,
    string? ForceSetCommand = null, bool ShowClearButton = false, string? ClearCommand = null,
    string? ErrorValidationSource = null, string? ErrorValidationPropertyName = null);

public sealed record GeometryCulvertFeatureRow(string ID, int FID, Dictionary<string, string> Values,
    GeometryCulvertCoordinate[] Polyline, Dictionary<string, string[]>? Errors = null);

public sealed record GeometryCulvertCoordinate(double X, double Y, string? Z = null, string? M = null);

/// <summary>The real valid extent of the geometry's currently associated terrain.</summary>
public sealed record GeometryCulvertExtent(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Terrain values sampled from the selected source barrel polyline.</summary>
public sealed record GeometryCulvertProfile(string BarrelID, string BarrelName, double Length,
    GeometryCulvertProfileSample[] Samples);

public sealed record GeometryCulvertProfileSample(double Distance, double X, double Y, double Elevation);

/// <summary>
/// Draft mutations are replayed against a fresh source model and remain in
/// memory until the parent geometry save includes this opaque state.
/// </summary>
public sealed record GeometryCulvertDraftState(string AssetPath, string Revision,
    string DependencyRevision, GeometryCulvertMutation[] Mutations);
