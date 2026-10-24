using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.OSM;

public class OsmRingSet
{
	public List<List<Point>> OuterRings { get; } = new List<List<Point>>();

	public List<List<Point>> InnerRings { get; } = new List<List<Point>>();

	public int UnclosedCount { get; set; }

	public int DegenerateCount { get; set; }

	public bool HasOuterRings => OuterRings.Count > 0;
}
