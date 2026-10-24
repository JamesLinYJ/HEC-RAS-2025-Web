using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.Topology;

public class TopologyDiff
{
	public readonly record struct NodeMatch(int IndexA, int IndexB, double Distance);

	public readonly record struct ArcMatch(int IndexA, int IndexB, bool IsReversed, double MaxDeviation);

	public readonly record struct ControlPointMatch(int IndexA, int IndexB, double Distance);

	public readonly record struct RegionMatch(int IndexA, int IndexB);

	public List<NodeMatch> MatchedNodes = new List<NodeMatch>();

	public List<int> AddedNodes = new List<int>();

	public List<int> RemovedNodes = new List<int>();

	public List<ArcMatch> MatchedArcs = new List<ArcMatch>();

	public List<int> AddedArcs = new List<int>();

	public List<int> RemovedArcs = new List<int>();

	public List<ControlPointMatch> MatchedControlPoints = new List<ControlPointMatch>();

	public List<int> AddedControlPoints = new List<int>();

	public List<int> RemovedControlPoints = new List<int>();

	public List<RegionMatch> MatchedRegions = new List<RegionMatch>();

	public List<int> AddedRegions = new List<int>();

	public List<int> RemovedRegions = new List<int>();

	private const int ArcSampleCount = 20;

	public static TopologyDiff Compute(Topology a, Topology b, double tolerance)
	{
		TopologyDiff topologyDiff = new TopologyDiff();
		MatchPoints(a.Nodes, b.Nodes, tolerance, (Node n) => n.Point, topologyDiff.MatchedNodes, topologyDiff.RemovedNodes, topologyDiff.AddedNodes);
		MatchArcsSpatially(a, b, tolerance, topologyDiff);
		MatchPoints(a.RegionControlPoints, b.RegionControlPoints, tolerance, (Point p) => p, topologyDiff.MatchedControlPoints, topologyDiff.RemovedControlPoints, topologyDiff.AddedControlPoints);
		MatchRegionsByArcs(a, b, topologyDiff);
		return topologyDiff;
	}

	private static void MatchPoints<TMatch, TA, TB>(IReadOnlyList<TA> listA, IReadOnlyList<TB> listB, double tolerance, Func<TA, Point> getPointA, Func<TB, Point> getPointB, List<TMatch> matched, List<int> removed, List<int> added, Func<int, int, double, TMatch> createMatch)
	{
		double tolerancePolynomial = tolerance * tolerance;
		List<(int, int, double)> valueTuples = new List<(int, int, double)>();
		for (int i = 0; i < listA.Count; i++)
		{
			Point point = getPointA(listA[i]);
			for (int j = 0; j < listB.Count; j++)
			{
				Point other = getPointB(listB[j]);
				double distanceSquaredTo = point.DistanceSquaredTo(other);
				if (distanceSquaredTo <= tolerancePolynomial)
				{
					valueTuples.Add((i, j, Math.Sqrt(distanceSquaredTo)));
				}
			}
		}
		valueTuples.Sort(((int idxA, int idxB, double dist) x, (int idxA, int idxB, double dist) y) => x.dist.CompareTo(y.dist));
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		foreach (var (localValue, localValue2, localValue3) in valueTuples)
		{
			if (!intSet.Contains(localValue) && !intSetSet.Contains(localValue2))
			{
				matched.Add(createMatch(localValue, localValue2, localValue3));
				intSet.Add(localValue);
				intSetSet.Add(localValue2);
			}
		}
		for (int ordinal = 0; ordinal < listA.Count; ordinal++)
		{
			if (!intSet.Contains(ordinal))
			{
				removed.Add(ordinal);
			}
		}
		for (int ordinal2 = 0; ordinal2 < listB.Count; ordinal2++)
		{
			if (!intSetSet.Contains(ordinal2))
			{
				added.Add(ordinal2);
			}
		}
	}

	private static void MatchPoints(IReadOnlyList<Node> listA, IReadOnlyList<Node> listB, double tolerance, Func<Node, Point> getPoint, List<NodeMatch> matched, List<int> removed, List<int> added)
	{
		MatchPoints(listA, listB, tolerance, getPoint, getPoint, matched, removed, added, (int a, int b, double d) => new NodeMatch(a, b, d));
	}

	private static void MatchPoints(IReadOnlyList<Point> listA, IReadOnlyList<Point> listB, double tolerance, Func<Point, Point> getPoint, List<ControlPointMatch> matched, List<int> removed, List<int> added)
	{
		MatchPoints(listA, listB, tolerance, getPoint, getPoint, matched, removed, added, (int a, int b, double d) => new ControlPointMatch(a, b, d));
	}

	private static void MatchArcsSpatially(Topology a, Topology b, double tolerance, TopologyDiff diff)
	{
		int count = a.Arcs.Count;
		int count2 = b.Arcs.Count;
		Polyline[] polylines = new Polyline[count2];
		Extent[] extents = new Extent[count2];
		for (int i = 0; i < count2; i++)
		{
			polylines[i] = b.GetArcPolyline(i);
			extents[i] = polylines[i].Extent;
		}
		List<(int, int, bool, double)> valueTuples = new List<(int, int, bool, double)>();
		for (int j = 0; j < count; j++)
		{
			Polyline arcPolyline = a.GetArcPolyline(j);
			if (!Polyline.IsValidPolyline(arcPolyline))
			{
				continue;
			}
			Extent extent = arcPolyline.Extent.Extend(tolerance);
			for (int k = 0; k < count2; k++)
			{
				Polyline polyline = polylines[k];
				if (Polyline.IsValidPolyline(polyline) && extent.Overlaps(in extents[k]) && TryMatchEndpoints(arcPolyline, polyline, tolerance, out var reversed))
				{
					double bidirectionalMaxDeviation = ComputeBidirectionalMaxDeviation(arcPolyline, polyline);
					if (bidirectionalMaxDeviation <= tolerance)
					{
						valueTuples.Add((j, k, reversed, bidirectionalMaxDeviation));
					}
				}
			}
		}
		valueTuples.Sort(((int idxA, int idxB, bool reversed, double maxDev) x, (int idxA, int idxB, bool reversed, double maxDev) y) => x.maxDev.CompareTo(y.maxDev));
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		foreach (var (localValue, localValue2, isReversed, maxDeviation) in valueTuples)
		{
			if (!intSet.Contains(localValue) && !intSetSet.Contains(localValue2))
			{
				diff.MatchedArcs.Add(new ArcMatch(localValue, localValue2, isReversed, maxDeviation));
				intSet.Add(localValue);
				intSetSet.Add(localValue2);
			}
		}
		for (int ordinal = 0; ordinal < count; ordinal++)
		{
			if (!intSet.Contains(ordinal))
			{
				diff.RemovedArcs.Add(ordinal);
			}
		}
		for (int ordinal2 = 0; ordinal2 < count2; ordinal2++)
		{
			if (!intSetSet.Contains(ordinal2))
			{
				diff.AddedArcs.Add(ordinal2);
			}
		}
	}

	private static bool TryMatchEndpoints(Polyline a, Polyline b, double tolerance, out bool reversed)
	{
		double tolerancePolynomial = tolerance * tolerance;
		Point point = a.First();
		Point point2 = a.Last();
		Point other = b.First();
		Point other2 = b.Last();
		if (point.DistanceSquaredTo(other) <= tolerancePolynomial && point2.DistanceSquaredTo(other2) <= tolerancePolynomial)
		{
			reversed = false;
			return true;
		}
		if (point.DistanceSquaredTo(other2) <= tolerancePolynomial && point2.DistanceSquaredTo(other) <= tolerancePolynomial)
		{
			reversed = true;
			return true;
		}
		reversed = false;
		return false;
	}

	private static double ComputeBidirectionalMaxDeviation(Polyline a, Polyline b)
	{
		double oneSidedMaxDeviation = ComputeOneSidedMaxDeviation(a, b);
		double oneSidedMaxDeviation2 = ComputeOneSidedMaxDeviation(b, a);
		return Math.Max(oneSidedMaxDeviation, oneSidedMaxDeviation2);
	}

	private static double ComputeOneSidedMaxDeviation(Polyline source, Polyline target)
	{
		double actualDistance2 = 0.0;
		for (int i = 0; i <= 20; i++)
		{
			double fractionAlongLine = (double)i / 20.0;
			PointOnPolyline pointOnPolyline = source.PointAtFraction(fractionAlongLine);
			if (!pointOnPolyline.IsNull())
			{
				target.NearestPointOnPolyline(pointOnPolyline.Point, out var actualDistance);
				if (actualDistance > actualDistance2)
				{
					actualDistance2 = actualDistance;
				}
			}
		}
		return actualDistance2;
	}

	private static void MatchRegionsByArcs(Topology a, Topology b, TopologyDiff diff)
	{
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		foreach (ArcMatch matchedArc in diff.MatchedArcs)
		{
			intByInt[matchedArc.IndexA] = matchedArc.IndexB;
		}
		int count = b.CompletedRegions.Count;
		HashSet<int>[] intSetArray = new HashSet<int>[count];
		for (int i = 0; i < count; i++)
		{
			Region region = b.CompletedRegions[i];
			HashSet<int> intSet = new HashSet<int>();
			if (region.OuterRing?.Arcs != null)
			{
				foreach (ArcWithDirection arc in region.OuterRing.Arcs)
				{
					intSet.Add(arc.ArcID);
				}
			}
			intSetArray[i] = intSet;
		}
		HashSet<int> intSetSet = new HashSet<int>();
		for (int j = 0; j < a.CompletedRegions.Count; j++)
		{
			Region region2 = a.CompletedRegions[j];
			HashSet<int> intSet2 = new HashSet<int>();
			bool condition = true;
			if (region2.OuterRing?.Arcs != null)
			{
				foreach (ArcWithDirection arc2 in region2.OuterRing.Arcs)
				{
					if (intByInt.TryGetValue(arc2.ArcID, out var localValue))
					{
						intSet2.Add(localValue);
						continue;
					}
					condition = false;
					break;
				}
			}
			if (!condition)
			{
				diff.RemovedRegions.Add(j);
				continue;
			}
			bool condition2 = false;
			for (int k = 0; k < count; k++)
			{
				if (!intSetSet.Contains(k) && intSetArray[k].SetEquals(intSet2))
				{
					diff.MatchedRegions.Add(new RegionMatch(j, k));
					intSetSet.Add(k);
					condition2 = true;
					break;
				}
			}
			if (!condition2)
			{
				diff.RemovedRegions.Add(j);
			}
		}
		for (int l = 0; l < count; l++)
		{
			if (!intSetSet.Contains(l))
			{
				diff.AddedRegions.Add(l);
			}
		}
	}
}
