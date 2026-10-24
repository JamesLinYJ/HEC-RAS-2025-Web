using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.OSM;

public static class OsmRingAssembler
{
	public const int MinimumRingVertices = 3;

	public static bool TryChainRings(OsmElement relation, out OsmRingSet rings)
	{
		rings = new OsmRingSet();
		if (relation == null || relation.Members == null)
		{
			return false;
		}
		List<List<Point>> pointListList = new List<List<Point>>();
		List<List<Point>> pointListListList = new List<List<Point>>();
		foreach (OsmMember member in relation.Members)
		{
			if (member == null || member.Geometry == null)
			{
				continue;
			}
			List<Point> toPoints = ToPoints(member.Geometry);
			if (toPoints.Count >= 2)
			{
				if (IsInnerRole(member.Role))
				{
					pointListListList.Add(toPoints);
				}
				else
				{
					pointListList.Add(toPoints);
				}
			}
		}
		ChainSegments(pointListList, rings.OuterRings, rings);
		ChainSegments(pointListListList, rings.InnerRings, rings);
		return rings.HasOuterRings;
	}

	public static bool TryGroupRings(IList<Polygon> outerRings, IList<Polygon> innerRings, out List<ComplexPolygon> polygons)
	{
		polygons = new List<ComplexPolygon>();
		if (outerRings == null || outerRings.Count == 0)
		{
			return false;
		}
		int innerRingsTerm = innerRings?.Count ?? 0;
		List<Polygon> polygonsList = new List<Polygon>(outerRings.Count + innerRingsTerm);
		foreach (Polygon outerRing in outerRings)
		{
			outerRing.EnsureClockwise();
			polygonsList.Add(outerRing);
		}
		if (innerRings != null)
		{
			foreach (Polygon innerRing in innerRings)
			{
				innerRing.EnsureCounterClockwise();
				polygonsList.Add(innerRing);
			}
		}
		if (ComplexPolygon.TryCreateComplexPolygons(polygonsList, out var complexPolys) && complexPolys.Count > 0)
		{
			polygons = complexPolys;
			return true;
		}
		foreach (Polygon outerRing2 in outerRings)
		{
			polygons.Add(ComplexPolygon.FromOuterRing(outerRing2));
		}
		return false;
	}

	public static bool IsInnerRole(string role)
	{
		return role == "inner";
	}

	private static List<Point> ToPoints(List<OsmCoordinate> geometry)
	{
		List<Point> points = new List<Point>(geometry.Count);
		foreach (OsmCoordinate geometry2 in geometry)
		{
			if (geometry2 == null)
			{
				continue;
			}
			Point point = new Point(geometry2.Lon, geometry2.Lat);
			if (points.Count > 0)
			{
				if (SamePoint(points[points.Count - 1], point))
				{
					continue;
				}
			}
			points.Add(point);
		}
		return points;
	}

	private static void ChainSegments(List<List<Point>> segments, List<List<Point>> rings, OsmRingSet tally)
	{
		bool[] boolValues = new bool[segments.Count];
		for (int i = 0; i < segments.Count; i++)
		{
			if (boolValues[i])
			{
				continue;
			}
			boolValues[i] = true;
			List<Point> segment = new List<Point>(segments[i]);
			while (!IsClosedChain(segment) && TryAppendMatchingSegment(segments, boolValues, segment))
			{
			}
			if (!IsClosedChain(segment))
			{
				tally.UnclosedCount++;
				continue;
			}
			segment.RemoveAt(segment.Count - 1);
			if (segment.Count < 3)
			{
				tally.DegenerateCount++;
			}
			else
			{
				rings.Add(segment);
			}
		}
	}

	private static bool TryAppendMatchingSegment(List<List<Point>> segments, bool[] used, List<Point> chain)
	{
		Point b = chain[chain.Count - 1];
		for (int i = 0; i < segments.Count; i++)
		{
			if (!used[i])
			{
				List<Point> segment = segments[i];
				if (SamePoint(segment[0], b))
				{
					used[i] = true;
					AppendForward(chain, segment);
					return true;
				}
				if (SamePoint(segment[segment.Count - 1], b))
				{
					used[i] = true;
					AppendReversed(chain, segment);
					return true;
				}
			}
		}
		return false;
	}

	private static void AppendForward(List<Point> chain, List<Point> segment)
	{
		for (int i = 1; i < segment.Count; i++)
		{
			chain.Add(segment[i]);
		}
	}

	private static void AppendReversed(List<Point> chain, List<Point> segment)
	{
		for (int countOffset = segment.Count - 2; countOffset >= 0; countOffset--)
		{
			chain.Add(segment[countOffset]);
		}
	}

	private static bool IsClosedChain(List<Point> chain)
	{
		if (chain.Count > 2)
		{
			return SamePoint(chain[0], chain[chain.Count - 1]);
		}
		return false;
	}

	private static bool SamePoint(Point a, Point b)
	{
		if (a.X == b.X)
		{
			return a.Y == b.Y;
		}
		return false;
	}
}
