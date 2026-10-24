namespace Ras.Backend;

/// <summary>
/// Captures the exact active Terrain source row and the two world-coordinate
/// endpoints used by the native equivalent of RasSlopeDelegate.
/// </summary>
public sealed record MapTerrainElevationCoordinate(double X, double Y);

public sealed record MapTerrainElevationRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator TerrainNode,
    string ExpectedTerrainPath,
    string ExpectedTerrainRevision,
    AssociatedMapLayerDependency[] ExpectedDependencies,
    MapTerrainElevationCoordinate[] Endpoints,
    MapResultGeometryOwner[]? ResultOwners = null);

public sealed record MapTerrainElevationResult(
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string VisualizationRevision,
    string MapID,
    int MapIndex,
    string MapName,
    string MapType,
    string ContextToken,
    CatalogTreeNodeLocator TerrainNode,
    string TerrainPath,
    string TerrainRevision,
    AssociatedMapLayerDependency[] SourceDependencies,
    double?[] Elevations);
