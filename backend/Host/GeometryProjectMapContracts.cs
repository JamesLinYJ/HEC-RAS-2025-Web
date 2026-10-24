namespace Ras.Backend;

public sealed record MapGeometryViewport(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>
/// Loads real geometry children from one saved map and one exact map-tree
/// locator. The locator root is map-specific: Project=Geometries,
/// Buildable=Layers, Geometry=Focused Geometries, Plan=Focused Plans or
/// Matching Layers. A Plan focused child sends its one exact parent Plan in
/// PlanNodes; a shared Matching Layers Geometry sends the complete ordered
/// Focused Plans set. Buildable and Geometry rows require a serialized
/// NodePath with at least a root and child index; Project defaults may have no
/// persisted path and are resolved by a unique exact root/name/canonical-asset
/// match. Identity is revalidated with map revisions.
/// </summary>
public sealed record MapGeometryRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator GeometryNode,
    MapGeometryViewport? Viewport = null,
    int PageSize = 512,
    string? ContextToken = null,
    string? TopologyCursor = null,
    string? CellCursor = null,
    string? CulvertCursor = null,
    bool SkipTopologyPage = false,
    bool SkipFlowAreasPage = false,
    bool SkipCulvertsPage = false,
    CatalogTreeNodeLocator[]? PlanNodes = null);

/// <summary>Source payload and revision identity for the exact requested map row.</summary>
public sealed record MapGeometrySnapshot(
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ProjectCanonicalPath,
    string ProjectRevision,
    string? ProjectXmlSHA,
    string VisualizationRevision,
    string ContextToken,
    CatalogTreeNodeLocator GeometryNode,
    string GeometryName,
    string GeometryCanonicalPath,
    string GeometryRevision,
    bool GeometryChecked,
    bool GeometryEffectiveVisible,
    GeometryTopologyExtent? Extent,
    MapGeometryLayerState TopologyState,
    MapGeometryLayerState FlowAreasState,
    MapGeometryLayerState CulvertsState,
    MapGeometryTopologyPage? Topology,
    MapGeometryFlowAreasPage? FlowAreas,
    MapGeometryCulvertsPage? Culverts,
    CatalogTreeNodeLocator[] PlanNodes);

public sealed record MapGeometryLayerState(
    string? NodeID,
    CatalogTreeNodeLocator? Locator,
    bool IsChecked,
    bool EffectiveVisible,
    string Status,
    string? Diagnostic);

public sealed record MapGeometryTopologyPage(
    GeometryTopologyExtent? SourceExtent,
    int SourceFeatureCount,
    int ScannedFeatureCount,
    int ReturnedFeatureCount,
    int InvalidFeatureCount,
    int OmittedSourceFeatureCount,
    GeometryTopologyMapFeature[] Features,
    bool Complete,
    string? NextCursor);

public sealed record MapGeometryFlowAreaSource(
    string ID,
    int RowIndex,
    string Name,
    double? TargetedCellSpacing,
    bool HasMesh,
    int CellCount,
    int FaceCount,
    int NodeCount,
    GeometryMeshExtent? Extent,
    bool HasMeshErrors,
    bool HasPropertyTables,
    bool PropertyTablesPinned,
    GeometryMeshMetadataItem[] Metadata);

public sealed record MapGeometryFlowAreasPage(
    GeometryTopologyExtent? SourceExtent,
    MapGeometryFlowAreaSource[] Areas,
    int SourceCellCount,
    int ScannedSourceCellCount,
    int ReturnedCellCount,
    int InvalidCellCount,
    GeometryMeshMapCell[] Cells,
    bool Complete,
    string? NextCursor);

public sealed record MapGeometryCulvertsPage(
    GeometryTopologyExtent? SourceExtent,
    int SourceBarrelCount,
    int ScannedSourceBarrelCount,
    int ReturnedBarrelCount,
    int InvalidFeatureCount,
    GeometryCulvertFeatureRow[] Features,
    bool Complete,
    string? NextCursor);
