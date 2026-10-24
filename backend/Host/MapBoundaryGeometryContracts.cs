namespace Ras.Backend;

/// <summary>
/// Exact Result/MultiResult selection used when a source sublayer is embedded
/// in a stored Result. MultiResult children are the current source keyframe
/// and therefore use selection slot zero only.
/// </summary>
public sealed record MapResultGeometryOwner(
    CatalogTreeNodeLocator ResultNode,
    string ContextToken,
    MapResultSelectionOverride[]? SelectionOverrides,
    int SelectionIndex,
    string SelectedResultPath,
    string SelectedResultRevision);

public sealed record MapResultOwnerIdentity(
    CatalogTreeNodeLocator ResultNode,
    string ContextToken,
    MapResultSelectionOverride[]? SelectionOverrides,
    int SelectionIndex,
    string SelectedResultPath,
    string SelectedResultRevision);

public sealed record MapBoundaryGeometryRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator BoundaryNode,
    string ExpectedBoundaryRevision,
    string? BoundarySourceState = null,
    string? ExpectedContextToken = null,
    MapResultGeometryOwner? ResultOwner = null,
    RasterMapViewExtent? AttachedFacesViewport = null,
    double RenderScale = 1.0);

/// <summary>
/// Read-only source line projection for a saved map. SourceOrdinal is the
/// original zero-based position in BoundaryConditionLineCollection.Features;
/// it is not a persisted ID and remains scoped to the captured boundary HDF.
/// </summary>
public sealed record MapBoundaryLineData(
    string Id,
    string Name,
    BoundaryLineCoordinateData[]? Coordinates,
    int SourceOrdinal);

public sealed record MapBoundaryGeometrySnapshot(
    string ProjectCanonicalPath,
    string ProjectRevision,
    string? ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ContextToken,
    CatalogTreeNodeLocator BoundaryNode,
    string BoundaryPath,
    string BoundaryRevision,
    bool BoundaryChecked,
    bool BoundaryEffectiveVisible,
    string BoundarySourceState,
    MapBoundaryLineData[] Lines,
    MapResultOwnerIdentity? ResultOwner,
    MapSourceRasterFrame? AttachedFaces = null);

public sealed record MapResultGeometryRequest(
    MapTreeRequest Map,
    MapResultGeometryOwner ResultOwner,
    CatalogTreeNodeLocator GeometryNode,
    string? GeometryContextToken = null,
    MapGeometryViewport? Viewport = null,
    int PageSize = 512,
    string? TopologyCursor = null,
    string? CellCursor = null,
    string? CulvertCursor = null,
    bool SkipTopologyPage = false,
    bool SkipFlowAreasPage = false,
    bool SkipCulvertsPage = false);

/// <summary>
/// Static geometry belonging to one exact selected Result's serialized
/// Geometry child. The nested map-geometry context token is scoped to that
/// child and is used for paging; ContextToken echoes the Result chart context.
/// </summary>
public sealed record MapResultGeometrySnapshot(
    string ProjectCanonicalPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ContextToken,
    CatalogTreeNodeLocator ResultNode,
    int SelectionIndex,
    string SelectedResultPath,
    string SelectedResultRevision,
    CatalogTreeNodeLocator GeometryNode,
    string GeometryPath,
    string GeometryRevision,
    MapGeometrySnapshot Geometry);

public sealed record MapResultCulvertProfileRequest(
    MapTreeRequest Map,
    string ContextToken,
    MapResultSelectionOverride[]? SelectionOverrides,
    CatalogTreeNodeLocator ResultNode,
    int SelectionIndex,
    int ProfileIndex);

public sealed record MapResultCulvertCoordinate(double X, double Y);

public sealed record MapResultCulvertBarrel(
    string ID,
    int SourceOrdinal,
    string Name,
    MapResultCulvertCoordinate[] Coordinates,
    string BarrelPropertyName,
    string UpstreamInvert,
    string DownstreamInvert,
    string UpstreamOpeningName,
    string DownstreamOpeningName);

public sealed record MapResultCulvertProfileSnapshot(
    string ProjectCanonicalPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ContextToken,
    CatalogTreeNodeLocator ResultNode,
    int SelectionIndex,
    string SelectionMode,
    string SelectedResultPath,
    string SelectedResultRevision,
    string? SelectedMapType,
    int ProfileIndex,
    int ProfileCount,
    string[] ProfileNames,
    string? GeometryName,
    string GeometryPath,
    string GeometryRevision,
    CatalogTreeNodeLocator? GeometryOwnerNode,
    bool GeometryAvailable,
    bool OutputAvailable,
    string? Diagnostic,
    MapResultCulvertBarrel[] Barrels,
    double[] Flow,
    double[] OutsideDepth,
    double[] OutletVelocity);
