using System.Collections.Generic;
using Geospatial.Features;

namespace Geospatial.IO.Network.OSM;

public class OsmFeatureSets
{
	public PointFeatureCollection Points { get; set; }

	public PolylineFeatureCollection Lines { get; set; }

	public ComplexPolygonFeatureCollection Polygons { get; set; }

	public int PointCount
	{
		get
		{
			if (Points != null)
			{
				return Points.Count;
			}
			return 0;
		}
	}

	public int LineCount
	{
		get
		{
			if (Lines != null)
			{
				return Lines.Count;
			}
			return 0;
		}
	}

	public int PolygonCount
	{
		get
		{
			if (Polygons != null)
			{
				return Polygons.Count;
			}
			return 0;
		}
	}

	public int TotalCount => PointCount + LineCount + PolygonCount;

	public bool HasAny => TotalCount > 0;

	public string DescribeCounts()
	{
		if (!HasAny)
		{
			return "No features found.";
		}
		List<string> stringList = new List<string>();
		if (PointCount > 0)
		{
			stringList.Add($"{PointCount:N0} {Pluralize("point", PointCount)}");
		}
		if (LineCount > 0)
		{
			stringList.Add($"{LineCount:N0} {Pluralize("line", LineCount)}");
		}
		if (PolygonCount > 0)
		{
			stringList.Add($"{PolygonCount:N0} {Pluralize("polygon", PolygonCount)}");
		}
		return string.Join(", ", stringList);
	}

	private static string Pluralize(string word, int count)
	{
		if (count != 1)
		{
			return word + "s";
		}
		return word;
	}
}
