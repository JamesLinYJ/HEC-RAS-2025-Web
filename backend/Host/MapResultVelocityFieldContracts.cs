namespace Ras.Backend;

/// <summary>
/// Samples the actual selected Result Velocity mapper on the active map's
/// screen raster. Selection and viewport identity are captured by the same
/// source snapshot as MapResultRenderService.
/// </summary>
public sealed record MapResultVelocityFieldRequest(
    MapTreeRequest Map,
    MapResultSelectionOverride[] SelectionOverrides,
    string ContextToken,
    CatalogTreeNodeLocator Node,
    string RequestID,
    string ExpectedResultPath,
    string ExpectedResultRevision,
    int ProfileIndex,
    RasterMapViewExtent Viewport,
    bool ForceHorizontalRendering = false);

/// <summary>
/// Interleaved little-endian float32 triples in row-major raster order:
/// vx, vy, magnitude. They are values returned by VelocityMapper.Compute.
/// </summary>
public sealed record MapResultVelocityFieldResult(
    string RequestID,
    string ContextToken,
    string SourceID,
    string SourceMapID,
    CatalogTreeNodeLocator Node,
    string ResultPath,
    string Revision,
    string ResultName,
    string TerrainPath,
    string TerrainRevision,
    string MapType,
    int SelectionIndex,
    int ProfileIndex,
    string ProfileName,
    string Time,
    int Width,
    int Height,
    double CellSize,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    float NoDataValue,
    byte[] InterleavedVelocity,
    bool ForceHorizontalRendering = false);
