namespace Ras.Backend;

/// <summary>
/// Requests source-rendered Geometry associations for exact rows in one saved
/// map tree. Geometry rows establish the retained source-object relationship;
/// association rows identify the tree contexts that are actually visible.
/// </summary>
public sealed record AssociatedMapLayersRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator[] GeometryNodes,
    CatalogTreeNodeLocator[] AssociationNodes,
    MapGeometryViewport Viewport,
    int Width,
    int Height,
    string? ContextToken = null,
    CatalogTreeNodeLocator[]? PlanNodes = null,
    MapResultGeometryOwner[]? ResultOwners = null,
    AssociatedMapLayerRasterRuntimeOption[]? RasterRuntimeOptions = null);

/// <summary>
/// Runtime-only RasterRenderLayer option bound to one exact map association
/// locator and its source HDF path. It is never written to the map .vis file.
/// </summary>
public sealed record AssociatedMapLayerRasterRuntimeOption(
    CatalogTreeNodeLocator Locator,
    string AssetPath,
    bool PlotRasterTileOutlines);

public sealed record AssociatedMapLayerDependency(string Path, string Revision, string Role);

/// <summary>Legend entry values retain the source's invariant textual form.</summary>
public sealed record AssociatedMapLayerLegend(
    string Value,
    string Label,
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha);

public sealed record AssociatedMapLayerSnapshot(
    CatalogTreeNodeLocator Locator,
    string Kind,
    string AssetPath,
    string Revision,
    AssociatedMapLayerDependency[] Dependencies,
    string Status,
    string? Diagnostic,
    bool IsChecked,
    bool EffectiveVisible,
    CatalogTreeNodeLocator[] GeometryNodes,
    int Width,
    int Height,
    byte[] Rgba,
    AssociatedMapLayerLegend[] Legend,
    MapGeometryViewport? SourceExtent = null,
    CatalogTreeNodeLocator[]? PlanNodes = null,
    MapResultGeometryOwner[]? ResultOwners = null,
    bool? PlotRasterTileOutlines = null);

public sealed record AssociatedMapLayersSnapshot(
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ProjectCanonicalPath,
    string ProjectRevision,
    string? ProjectXmlSHA,
    string VisualizationRevision,
    string ContextToken,
    MapGeometryViewport Viewport,
    AssociatedMapLayerSnapshot[] Layers);
