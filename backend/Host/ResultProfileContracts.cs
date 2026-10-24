namespace Ras.Backend;

/// <summary>Identifies a read-only source Result by its canonical path and revision.</summary>
public sealed record ResultProfileCatalogRequest(string ResultPath, string Revision);

/// <summary>Requests one stored element through all profiles in a Result output block.</summary>
public sealed record ResultProfileRequest(string ResultPath, string Revision,
    string OutputBlockName, string Variable, int FlowAreaIndex, int ElementIndex);

/// <summary>
/// Requests exact, selectable source mesh geometry for one embedded Result
/// flow area. Extent is expressed in the Result mesh's original coordinates.
/// A null extent requests the full mesh extent.
/// </summary>
public sealed record ResultProfileMapRequest(string ResultPath, string Revision,
    int FlowAreaIndex, string ElementKind, GeometryTopologyExtent? Extent = null, int Limit = 2500);

public sealed record ResultProfileVariable(string Id, string Name, string ElementKind, string Quantity, string Units);
public sealed record ResultProfileOutputBlock(string Name, int ProfileCount,
    string[] ProfileNames, string[] Times, ResultProfileVariable[] Variables);
public sealed record ResultProfileFlowArea(int Index, string Name, int CellCount, int FaceCount);
public sealed record ResultProfileCatalogResult(string ResultPath, string Revision, string ResultName,
    ResultProfileOutputBlock[] OutputBlocks, ResultProfileFlowArea[] FlowAreas);
public sealed record ResultProfilePoint(int ProfileIndex, string ProfileName, string Time, double? Value);
public sealed record ResultProfileSeriesResult(string ResultPath, string Revision, string ResultName,
    string OutputBlockName, string Variable, string FlowAreaName, int FlowAreaIndex,
    int ElementIndex, string ElementKind, string Quantity, string Units,
    bool Available, string? UnavailableReason,
    ResultProfilePoint[] Points);

/// <summary>
/// Exact Result mesh features within the requested map extent. An oversized
/// extent returns no partial features and sets RequiresZoom; ElementIndex is
/// the Result mesh-local array ordinal used by the stored HDF column reader.
/// </summary>
public sealed record ResultProfileMapResult(string ResultPath, string Revision,
    string FlowAreaName, int FlowAreaIndex, string ElementKind,
    bool Available, string? UnavailableReason,
    GeometryTopologyExtent? SourceExtent, GeometryTopologyExtent? RequestedExtent,
    int TotalElements, int? IntersectingElements, int ReturnedElements,
    int ElementLimit, int VertexLimit, bool IsComplete, bool RequiresZoom,
    ResultProfileMapElement[] Elements);

public sealed record ResultProfileMapElement(int ElementIndex, GeometryTopologyCoordinate[][] Paths);
