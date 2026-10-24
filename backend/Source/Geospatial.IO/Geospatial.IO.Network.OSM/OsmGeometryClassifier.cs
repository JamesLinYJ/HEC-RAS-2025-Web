using System.Collections.Generic;

namespace Geospatial.IO.Network.OSM;

public static class OsmGeometryClassifier
{
	public const string ExplicitAreaKey = "area";

	public const string AreaYes = "yes";

	public const string AreaNo = "no";

	public const string RelationTypeKey = "type";

	public const string RelationTypeMultipolygon = "multipolygon";

	public const string RelationTypeBoundary = "boundary";

	private const string NaturalKey = "natural";

	private const string PowerKey = "power";

	private const string WaterwayKey = "waterway";

	private static readonly HashSet<string> AreaKeys = new HashSet<string>
	{
		"building", "building:part", "landuse", "leisure", "amenity", "water", "wetland", "place", "boundary", "man_made",
		"historic", "military", "shop", "tourism", "landcover", "golf", "public_transport"
	};

	private static readonly HashSet<string> LinearKeys = new HashSet<string> { "highway", "railway", "barrier" };

	private static readonly HashSet<string> NaturalLineValues = new HashSet<string> { "coastline", "tree_row", "ridge", "arete", "cliff" };

	private static readonly HashSet<string> PowerAreaValues = new HashSet<string> { "plant", "substation", "generator" };

	private static readonly HashSet<string> WaterwayAreaValues = new HashSet<string> { "riverbank", "dock" };

	public static OsmGeometryKind Classify(OsmElement element)
	{
		if (element == null)
		{
			return OsmGeometryKind.None;
		}
		if (element.IsNode)
		{
			return OsmGeometryKind.Point;
		}
		if (element.IsWay)
		{
			if (IsClosed(element.Geometry) && IsAreaTagged(element))
			{
				return OsmGeometryKind.Polygon;
			}
			return OsmGeometryKind.Line;
		}
		if (element.IsRelation)
		{
			if (!IsAreaRelation(element))
			{
				return OsmGeometryKind.Line;
			}
			return OsmGeometryKind.Polygon;
		}
		return OsmGeometryKind.None;
	}

	public static bool IsClosed(List<OsmCoordinate> geometry)
	{
		if (geometry == null || geometry.Count < 4)
		{
			return false;
		}
		OsmCoordinate osmCoordinate = geometry[0];
		OsmCoordinate osmCoordinate2 = geometry[geometry.Count - 1];
		if (osmCoordinate == null || osmCoordinate2 == null)
		{
			return false;
		}
		if (osmCoordinate.Lat == osmCoordinate2.Lat)
		{
			return osmCoordinate.Lon == osmCoordinate2.Lon;
		}
		return false;
	}

	public static bool IsAreaTagged(OsmElement element)
	{
		if (element == null || element.Tags == null)
		{
			return false;
		}
		if (element.TryGetTag("area", out var localValue))
		{
			if (localValue == "yes")
			{
				return true;
			}
			if (localValue == "no")
			{
				return false;
			}
		}
		if (ContainsAnyKey(element.Tags, LinearKeys))
		{
			return false;
		}
		if (element.TryGetTag("waterway", out var localValue2))
		{
			return WaterwayAreaValues.Contains(localValue2);
		}
		if (ContainsAnyKey(element.Tags, AreaKeys))
		{
			return true;
		}
		if (element.TryGetTag("natural", out var localValue3))
		{
			return !NaturalLineValues.Contains(localValue3);
		}
		if (element.TryGetTag("power", out var localValue4))
		{
			return PowerAreaValues.Contains(localValue4);
		}
		return false;
	}

	public static bool IsAreaRelation(OsmElement element)
	{
		if (element == null || !element.IsRelation)
		{
			return false;
		}
		if (!element.TryGetTag("type", out var localValue))
		{
			return false;
		}
		if (!(localValue == "multipolygon"))
		{
			return localValue == "boundary";
		}
		return true;
	}

	private static bool ContainsAnyKey(Dictionary<string, string> tags, HashSet<string> keys)
	{
		foreach (string key in keys)
		{
			if (tags.ContainsKey(key))
			{
				return true;
			}
		}
		return false;
	}
}
