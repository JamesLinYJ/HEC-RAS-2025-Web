namespace Ras.Backend;

/// <summary>One stable file in a file geodatabase snapshot. OGR lock files are excluded.</summary>
public sealed record GdbMapSourceFileRevision(string RelativePath, long Length, string Revision);

/// <summary>The canonical directory and complete non-lock-file content manifest for one .gdb.</summary>
public sealed record GdbMapSourceIdentity(
    string SourcePath,
    string Revision,
    GdbMapSourceFileRevision[] Files);

/// <summary>One retained vector layer discovered in a file geodatabase.</summary>
public sealed record GdbMapLayerDescriptor(
    string Name,
    string GeometryType,
    int GeometryTypeCode,
    string VectorType,
    long FeatureCount,
    string? ProjectionWKT);

public sealed record GdbMapCatalogRequest(string SourcePath, string? ExpectedSourceRevision = null);
public sealed record GdbMapCatalogResponse(
    GdbMapSourceIdentity Source,
    GdbMapLayerDescriptor[] Layers,
    GdbRasterSubdatasetDescriptor[]? RasterSubdatasets = null);

public sealed record GdbMapImportRequest(
    MapDocumentRequest Map,
    string ExpectedProjectRevision,
    GdbMapSourceIdentity Source);

public sealed record GdbMapImportResponse(
    MapDocumentMutationReceipt Receipt,
    GdbMapSourceIdentity Source,
    GdbMapLayerDescriptor[] ImportedLayers,
    GdbRasterSubdatasetDescriptor[]? ImportedRasterSubdatasets = null);

public sealed record GdbMapSourceLoadRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    string ExpectedSourceRevision);

/// <summary>A source coordinate and its corresponding project-CRS drawing coordinate.</summary>
public sealed record GdbMapSourceVertex(
    double X,
    double Y,
    double SourceX,
    double SourceY,
    double? Z,
    double? M);

/// <summary>A polygon ring with its actual OGR role, retaining the exterior/interior relation.</summary>
public sealed record GdbMapSourceRing(string Role, GdbMapSourceVertex[] Vertices);

/// <summary>
/// Recursive OGR geometry. Points and lines use Vertices; polygons use Rings;
/// multi-geometries and geometry collections use Children.
/// </summary>
public sealed record GdbMapSourceGeometry(
    string GeometryType,
    int GeometryTypeCode,
    GdbMapSourceVertex[] Vertices,
    GdbMapSourceRing[] Rings,
    GdbMapSourceGeometry[] Children);

/// <summary>One source feature in stable OGR iteration order, with typed ordered fields.</summary>
public sealed record GdbMapSourceFeature(
    int SourceOrdinal,
    string? SourceID,
    GdbMapSourceGeometry Geometry,
    GdbMapSourceAttribute[] Attributes);

/// <summary>
/// One OGR field, retaining its declared type and the distinction between an
/// unset field and an explicitly stored SQL NULL. Value is an invariant
/// lexical form; lists are JSON arrays and binary values are base64.
/// </summary>
public sealed record GdbMapSourceAttribute(
    int ColumnOrdinal,
    string ColumnName,
    string Type,
    string? Value,
    bool? BooleanValue,
    bool IsSet,
    bool IsNull,
    int? TimeZoneFlag = null);

/// <summary>Actual GDB geometry, projected for drawing while preserving original XYZM coordinates.</summary>
public sealed record GdbMapSourceData(
    string GeometryType,
    int GeometryTypeCode,
    int Count,
    MapVectorSourceExtent? SourceExtent,
    MapVectorSourceExtent? Extent,
    string ProjectionStatus,
    string? SourceProjectionWKT,
    string? TargetProjectionWKT,
    GdbMapSourceFeature[] Features);

/// <summary>Exact persisted GDB child plus the source directory and layer capture used for this read.</summary>
public sealed record GdbMapSourceLoadResponse(
    MapSourceMapCapture Map,
    CatalogTreeNodeLocator Node,
    GdbMapSourceIdentity Source,
    GdbMapLayerDescriptor Layer,
    GdbMapSourceData Data);
