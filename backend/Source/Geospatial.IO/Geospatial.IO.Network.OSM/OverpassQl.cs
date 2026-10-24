using System.Collections.Generic;
using System.Globalization;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.OSM;

public static class OverpassQl
{
	public const char OneOfDelimiter = ';';

	private const string CoordinateFormat = "0.#######";

	public static string EscapeLiteral(string stringValue)
	{
		if (string.IsNullOrEmpty(stringValue))
		{
			return string.Empty;
		}
		return stringValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	public static string FormatBoundingBox(Extent extentWgs84)
	{
		string minY = extentWgs84.MinY.ToString("0.#######", CultureInfo.InvariantCulture);
		string minX = extentWgs84.MinX.ToString("0.#######", CultureInfo.InvariantCulture);
		string maxY = extentWgs84.MaxY.ToString("0.#######", CultureInfo.InvariantCulture);
		string maxX = extentWgs84.MaxX.ToString("0.#######", CultureInfo.InvariantCulture);
		return $"{minY},{minX},{maxY},{maxX}";
	}

	public static IEnumerable<string> GetKindTokens(OsmElementKinds kinds)
	{
		if (kinds == OsmElementKinds.All)
		{
			yield return "nwr";
			yield break;
		}
		if (kinds.HasFlag(OsmElementKinds.Node))
		{
			yield return "node";
		}
		if (kinds.HasFlag(OsmElementKinds.Way))
		{
			yield return "way";
		}
		if (kinds.HasFlag(OsmElementKinds.Relation))
		{
			yield return "relation";
		}
	}
}
