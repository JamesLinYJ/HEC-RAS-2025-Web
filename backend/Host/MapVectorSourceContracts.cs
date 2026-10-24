using System.Text.Json.Serialization;

namespace Ras.Backend;

/// <summary>Requests a real vector read for one exact persisted map layer.</summary>
public sealed record MapVectorSourceLoadRequest(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    MapSourceFamilyCapture? ExpectedFamily);

/// <summary>One finite extent in either source or target map coordinates.</summary>
public sealed record MapVectorSourceExtent(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>A source vertex with its original coordinates and project-space drawing coordinates.</summary>
public sealed record MapVectorSourceVertex(
    double X,
    double Y,
    double SourceX,
    double SourceY,
    double? Z,
    double? M);

/// <summary>A DBF cell whose ordinal and type are retained independently of its column name.</summary>
public sealed record MapVectorSourceAttribute(
    int ColumnOrdinal,
    string ColumnName,
    string Type,
    string? Value,
    bool? BooleanValue);

/// <summary>An ordered DBF field declaration retained even when its table has no rows.</summary>
public sealed record MapVectorAttributeField(
    int Ordinal,
    string Name,
    string SourceType,
    int Width,
    int DecimalCount,
    string ValueType);

/// <summary>One ordered path or polygon ring belonging to a source record.</summary>
public sealed record MapVectorSourcePart(
    int PartOrdinal,
    MapVectorSourceVertex[] Vertices,
    bool HasZ,
    bool HasM,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PolygonIndex = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsHole = null);

/// <summary>One vector record; Shapefile records have no intrinsic ID, so SourceID is null.</summary>
public sealed record MapVectorSourceFeature(
    int SourceOrdinal,
    string? SourceID,
    string ShapeType,
    MapVectorSourcePart[] Parts,
    MapVectorSourceAttribute[] Attributes);

/// <summary>Actual geometry and ordered attributes read from one Shapefile or GeoJSON source family.</summary>
public sealed record MapVectorSourceData(
    string ShapeType,
    int ShapeTypeCode,
    int Count,
    MapVectorSourceExtent? SourceExtent,
    MapVectorSourceExtent? Extent,
    string ProjectionStatus,
    string? SourceProjectionWKT,
    string? TargetProjectionWKT,
    MapVectorSourceFeature[] Features,
    MapVectorAttributeField[]? AttributeSchema = null);

/// <summary>The exact persisted map-node and consumed source-family capture used for this read.</summary>
public sealed record MapVectorSourceLoadResult(MapSourceNodeCapture Source, MapVectorSourceData Data);
