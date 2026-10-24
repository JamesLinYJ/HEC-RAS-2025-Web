namespace Ras.Backend;

/// <summary>
/// Captures one exact, active Wind BoundaryCondition row. BoundaryCondition
/// bytes are stable for this request; the selected Wind profile is taken from
/// that row's real source resampler.
/// </summary>
public sealed record MapWindVelocityFieldRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator BoundaryNode,
    CatalogTreeNodeLocator WindNode,
    string ExpectedBoundaryPath,
    string ExpectedBoundaryRevision,
    string ContextToken,
    string RequestID,
    int ProfileIndex,
    string? ExpectedProfileTime,
    RasterMapViewExtent Viewport);

public sealed record MapWindVelocityProfile(int Index, string Time);

/// <summary>Interleaved little-endian float32 triples: Vx, Vy, magnitude.</summary>
public sealed record MapWindVelocityFieldResult(
    string RequestID,
    string ContextToken,
    string SourceID,
    string SourceMapID,
    CatalogTreeNodeLocator BoundaryNode,
    CatalogTreeNodeLocator WindNode,
    string BoundaryPath,
    string BoundaryRevision,
    string SpatialDataType,
    int ProfileIndex,
    string ProfileName,
    string Time,
    MapWindVelocityProfile[] Profiles,
    int Width,
    int Height,
    double CellSize,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    float? NoDataValue,
    byte[] InterleavedVelocity);
