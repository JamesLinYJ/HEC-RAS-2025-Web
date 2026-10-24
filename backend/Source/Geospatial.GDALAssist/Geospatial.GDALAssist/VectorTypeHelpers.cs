using OSGeo.OGR;

namespace Geospatial.GDALAssist;

public static class VectorTypeHelpers
{
	public static VectorType Parse(wkbGeometryType type)
	{
		switch (type)
		{
		case wkbGeometryType.wkbMultiPolygon25D:
		case wkbGeometryType.wkbMultiPolygon:
		case wkbGeometryType.wkbMultiPolygonM:
		case wkbGeometryType.wkbMultiPolygonZM:
			return VectorType.PolygonCollection;
		case wkbGeometryType.wkbPolygon25D:
		case wkbGeometryType.wkbPolygon:
		case wkbGeometryType.wkbPolygonM:
		case wkbGeometryType.wkbPolygonZM:
			return VectorType.Polygon;
		case wkbGeometryType.wkbMultiLineString25D:
		case wkbGeometryType.wkbMultiLineString:
		case wkbGeometryType.wkbMultiLineStringM:
		case wkbGeometryType.wkbMultiLineStringZM:
			return VectorType.LineCollection;
		case wkbGeometryType.wkbLineString25D:
		case wkbGeometryType.wkbLineString:
		case wkbGeometryType.wkbLineStringM:
		case wkbGeometryType.wkbLineStringZM:
			return VectorType.Line;
		case wkbGeometryType.wkbMultiPoint25D:
		case wkbGeometryType.wkbMultiPoint:
		case wkbGeometryType.wkbMultiPointM:
		case wkbGeometryType.wkbMultiPointZM:
			return VectorType.PointCollection;
		case wkbGeometryType.wkbPoint25D:
		case wkbGeometryType.wkbPoint:
		case wkbGeometryType.wkbPointM:
		case wkbGeometryType.wkbPointZM:
			return VectorType.Point;
		case wkbGeometryType.wkbNone:
			return VectorType.None;
		default:
			return VectorType.Unknown;
		}
	}
}
