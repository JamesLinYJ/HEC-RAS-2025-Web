namespace Ras.Backend;

/// <summary>
/// One pointer sample from an exact visible map-source capture. The point and
/// probe point are in project coordinates; the latter lets the retained
/// DynamicProfile resampler keep its source polyline contract while sampling
/// the first coordinate exactly.
/// </summary>
public sealed record MapHoverIdentifyRequest(
    MapTreeRequest Map,
    string ContextToken,
    MapResultSelectionOverride[]? SelectionOverrides,
    string RequestID,
    MapHoverCoordinate Point,
    MapHoverCoordinate ProbePoint,
    double WorldUnitsPerScreenPoint,
    MapDynamicProfileSelection[]? ProfileSelections,
    MapHoverLayerCapture[] Layers);

public sealed record MapHoverCoordinate(double X, double Y);

/// <summary>
/// The source node and its data revision are sent separately from its display
/// name. A raster or mesh capture contains the renderer's own exact source
/// descriptor and is revalidated against the current map before sampling.
/// </summary>
public sealed record MapHoverLayerCapture(
    CatalogTreeNodeLocator Node,
    string Kind,
    string? SourcePath = null,
    string? SourceRevision = null,
    string? MapType = null,
    int? ProfileIndex = null,
    RasterMapSourceIdentity? RasterSource = null,
    MapHoverMeshCapture? Mesh = null,
    MapHoverAssociatedCapture? Associated = null,
    MapHoverResultSelectionCapture[]? ResultSelections = null,
    MapHoverModelVectorCapture? ModelVector = null,
    MapHoverPrecipitationCapture? Precipitation = null);

public sealed record MapHoverPrecipitationCapture(
    CatalogTreeNodeLocator BoundaryNode,
    string CaptureToken,
    string ExpectedProfileTime,
    MapResultGeometryOwner? ResultOwner = null);

public sealed record MapHoverResultSelectionCapture(int SelectionIndex, string ResultPath,
    string ResultRevision);

public sealed record MapHoverAssociatedCapture(
    CatalogTreeNodeLocator[] GeometryNodes,
    CatalogTreeNodeLocator[] PlanNodes,
    MapResultGeometryOwner[] ResultOwners,
    AssociatedMapLayerDependency[] Dependencies);

public sealed record MapHoverMeshCapture(
    CatalogTreeNodeLocator Node,
    CatalogTreeNodeLocator GeometryNode,
    string SourceIdentity,
    string GeometryContextToken,
    string SourcePath,
    string SourceRevision,
    CatalogTreeNodeLocator[]? PlanNodes,
    MapResultGeometryOwner? ResultOwner,
    GeometryMeshModelRequest? Model,
    string OwnerMode,
    string MeshDraftState,
    string MeshDraftSHA256,
    string? TopologyDraftState,
    string TopologyDraftSHA256,
    bool FlowAreaVisibilityOverride = false);

public sealed record MapHoverIdentifyEntry(
    CatalogTreeNodeLocator Node,
    string Kind,
    string Label,
    string? Value,
    string? ValueType,
    string? SourcePath,
    string? SourceRevision,
    string? MapType,
    int? ProfileIndex,
    int? MeshIndex,
    int? ElementIndex,
    string? ValueUnit = null);

public sealed record MapHoverIdentifyResult(
    string RequestID,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ContextToken,
    MapHoverCoordinate Point,
    MapHoverIdentifyEntry[] Entries);
