using System;

namespace Geospatial.IO.Vectors;

public static class GeoJsonTypes
{
	public const string FeatureCollection = "FeatureCollection";

	public const string Feature = "Feature";

	public const string Point = "Point";

	public const string MultiPoint = "MultiPoint";

	public const string LineString = "LineString";

	public const string MultiLineString = "MultiLineString";

	public const string Polygon = "Polygon";

	public const string MultiPolygon = "MultiPolygon";

	private const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

	public static bool Matches(string type, string expected)
	{
		return string.Equals(type, expected, StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsPointType(string type)
	{
		if (!Matches(type, "Point"))
		{
			return Matches(type, "MultiPoint");
		}
		return true;
	}

	public static bool IsLineType(string type)
	{
		if (!Matches(type, "LineString"))
		{
			return Matches(type, "MultiLineString");
		}
		return true;
	}

	public static bool IsPolygonType(string type)
	{
		if (!Matches(type, "Polygon"))
		{
			return Matches(type, "MultiPolygon");
		}
		return true;
	}

	public static bool IsMultiPoint(string type)
	{
		return Matches(type, "MultiPoint");
	}

	public static bool IsMultiLineString(string type)
	{
		return Matches(type, "MultiLineString");
	}

	public static bool IsMultiPolygon(string type)
	{
		return Matches(type, "MultiPolygon");
	}
}
