namespace Ras.Backend;

/// <summary>
/// Inspect one exact precipitation child from an accepted map tree. The map
/// payload remains the complete existing MapTreeRequest; chart Result
/// overrides and the nested Result owner are kept as separate identities.
/// </summary>
public sealed record MapPrecipitationInspectRequest(
    MapTreeRequest Map,
    MapResultSelectionOverride[]? SelectionOverrides,
    CatalogTreeNodeLocator BoundaryNode,
    CatalogTreeNodeLocator PrecipitationNode,
    string ExpectedSourcePath,
    string RequestID,
    MapResultGeometryOwner? ResultOwner = null,
    string? ExpectedMapContextToken = null,
    string? ExpectedCaptureToken = null,
    string? ExpectedSourceRevision = null);

public sealed record MapPrecipitationProfile(int Index, string Time);

/// <summary>
/// Finite raster bounds when known. Infinite constant sources are represented
/// by IsInfinite=true and null bounds instead of serializing Infinity values.
/// </summary>
public sealed record MapPrecipitationExtent(
    double? MinX,
    double? MinY,
    double? MaxX,
    double? MaxY,
    bool IsInfinite);

public sealed record MapPrecipitationInspectResult(
    string RequestID,
    string Status,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string MapContextToken,
    string CaptureToken,
    string CoreLayerContextIdentity,
    CatalogTreeNodeLocator BoundaryNode,
    CatalogTreeNodeLocator PrecipitationNode,
    string SourceMode,
    string SourcePath,
    string SourceRevision,
    MapResultGeometryOwner? ResultOwner,
    bool IsEnabled,
    bool IsChecked,
    bool IsEffectivelyVisible,
    string SpatialDataType,
    string TimelineKind,
    bool IsTemporal,
    int ProfileCount,
    MapPrecipitationProfile[] Profiles,
    string Unit,
    float? NoDataValue,
    MapPrecipitationExtent? ValidExtent);

/// <summary>
/// Render one captured native precipitation resampler profile into the
/// existing bounded RGBA source-frame contract. Profile state is transient.
/// </summary>
public sealed record MapPrecipitationFrameRequest(
    MapTreeRequest Map,
    MapResultSelectionOverride[]? SelectionOverrides,
    CatalogTreeNodeLocator BoundaryNode,
    CatalogTreeNodeLocator PrecipitationNode,
    MapResultGeometryOwner? ResultOwner,
    string ExpectedMapContextToken,
    string ExpectedCaptureToken,
    string ExpectedSourcePath,
    string ExpectedSourceRevision,
    string RequestID,
    int ProfileIndex,
    string ExpectedProfileTime,
    RasterMapViewExtent Viewport,
    double RenderScale = 1.0);

public sealed record MapPrecipitationFrameResult(
    string RequestID,
    string Status,
    string MapContextToken,
    string CaptureToken,
    string SourcePath,
    string SourceRevision,
    int ProfileIndex,
    string ProfileTime,
    string Unit,
    MapSourceRasterFrame? Frame);
