using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Geospatial.DataStructures;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public static class TopologyHelpers
{
	public struct SimpleNodeConnectivity
	{
		public int FirstArcFid = -1;

		public bool FirstArcIsStart = false;

		public int SecondArcFid = -1;

		public bool SecondArcIsStart = false;

		public bool DefinitelyNeeded = false;

		public SimpleNodeConnectivity()
		{
		}
	}

	private struct OwnedLineIntersection : IEquatable<OwnedLineIntersection>
	{
		public class Comparer : IComparer<OwnedLineIntersection>
		{
			int IComparer<OwnedLineIntersection>.Compare(OwnedLineIntersection x, OwnedLineIntersection y)
			{
				return PointOnPolyline.Compare(x.GetPOP(), y.GetPOP());
			}
		}

		public int LineIndex;

		public int OtherLineIndex;

		public bool IsAIntersection;

		public LineIntersection Intersection;

		public Point Point => Intersection.Point;

		public PointOnPolyline GetPOP()
		{
			if (!IsAIntersection)
			{
				return Intersection.BIntersection;
			}
			return Intersection.AIntersection;
		}

		public PointOnPolyline GetOtherPOP()
		{
			if (!IsAIntersection)
			{
				return Intersection.AIntersection;
			}
			return Intersection.BIntersection;
		}

		public bool Equals(OwnedLineIntersection other)
		{
			if (LineIndex == other.LineIndex && OtherLineIndex == other.OtherLineIndex)
			{
				return Intersection.Equals(other.Intersection);
			}
			return false;
		}

		public static implicit operator Point(OwnedLineIntersection owli)
		{
			return owli.Point;
		}
	}

	public struct TopologyImportOptions
	{
		public enum ArcMetadataImportOption
		{
			NoImport,
			ImportPrioritizeExisting,
			ImportPrioritizeImported
		}

		public double MinimumArcLength = 0.0001;

		public double NodeSnapToNodeDistance = 0.0001;

		public double NodeSnapToVerticeFraction = 0.999;

		public bool RemoveHangingArcs = false;

		public bool RemoveFloatingArcs = false;

		public bool PostMergeArcs = true;

		public bool RemoveCollinearInternalPoints = true;

		public double FractionOfSquareRootAreaTinTolerance = 1E-05;

		public ArcMetadataImportOption ImportArcMetadata = ArcMetadataImportOption.ImportPrioritizeExisting;

		public static TopologyImportOptions Default => new TopologyImportOptions();

		public TopologyImportOptions()
		{
		}
	}

	private struct TrianglePointPair(int ptIdx, int triIdx)
	{
		public class Comparer : IEqualityComparer<TrianglePointPair>
		{
			bool IEqualityComparer<TrianglePointPair>.Equals(TrianglePointPair x, TrianglePointPair y)
			{
				return x.Equals(y);
			}

			int IEqualityComparer<TrianglePointPair>.GetHashCode(TrianglePointPair trianglePointPair)
			{
				return trianglePointPair.PointIndex;
			}
		}

		public static TrianglePointPair Null = new TrianglePointPair(-1, -1);

		public int TriangleIndex = triIdx;

		public int PointIndex = ptIdx;

		public bool IsNull()
		{
			if (TriangleIndex == -1)
			{
				return PointIndex == -1;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return PointIndex;
		}

		public override bool Equals([NotNullWhen(true)] object obj)
		{
			if (!(obj is TrianglePointPair trianglePointPair))
			{
				return false;
			}
			return PointIndex == trianglePointPair.PointIndex;
		}

		public bool Equals(TrianglePointPair other)
		{
			return PointIndex == other.PointIndex;
		}
	}

	private struct ConstrainedEdge(EdgeDefinition edge, int constrainedIndex)
	{
		public EdgeDefinition Edge = edge;

		public int ConstrainedIndex = constrainedIndex;
	}

	public struct TinImportResult
	{
		public int AddedArcCount;

		public int NodeCountDelta;

		public List<int> RemovedArcFIDs;
	}

	public static void ImportPolylinesAsArcs_Intersection(this Topology topo, IList<Polyline> importLines, TopologyImportOptions opts)
	{
		List<Polyline> polylines = new List<Polyline>(importLines.Count);
		Extent ext = Extent.NullExtent;
		foreach (Polyline importLine in importLines)
		{
			if (importLine.Length >= opts.MinimumArcLength)
			{
				polylines.Add(importLine);
				ext = ext.Union(importLine.Extent);
			}
		}
		List<LineCollectionIntersection> allIntersections = PolylineCollection.GetAllIntersections(polylines);
		Dictionary<int, List<OwnedLineIntersection>> reverseLookup = new Dictionary<int, List<OwnedLineIntersection>>();
		foreach (LineCollectionIntersection allIntersection in allIntersections)
		{
			List<OwnedLineIntersection> orMakePlReverseLookup = GetOrMakePlReverseLookup(allIntersection.APolylineIndex);
			List<OwnedLineIntersection> orMakePlReverseLookupList = GetOrMakePlReverseLookup(allIntersection.BPolylineIndex);
			orMakePlReverseLookup.Add(new OwnedLineIntersection
			{
				Intersection = allIntersection.Intersection,
				IsAIntersection = true,
				LineIndex = allIntersection.APolylineIndex,
				OtherLineIndex = allIntersection.BPolylineIndex
			});
			orMakePlReverseLookupList.Add(new OwnedLineIntersection
			{
				Intersection = allIntersection.Intersection,
				IsAIntersection = false,
				LineIndex = allIntersection.BPolylineIndex,
				OtherLineIndex = allIntersection.APolylineIndex
			});
		}
		OwnedLineIntersection.Comparer comparer = new OwnedLineIntersection.Comparer();
		foreach (List<OwnedLineIntersection> ownedLineIntersections in reverseLookup.Values)
		{
			ownedLineIntersections.Sort(comparer);
		}
		List<SimpleNodeConnectivity> nodeConnectivity = new List<SimpleNodeConnectivity>(polylines.Count * 2);
		SpatialIndex<int> ptsSpatialIndex = new SpatialIndex<int>(ext);
		for (int i = 0; i < polylines.Count; i++)
		{
			Polyline polyline = polylines[i];
			List<OwnedLineIntersection> ownedLineIntersectionsList = null;
			reverseLookup.TryGetValue(i, out ownedLineIntersectionsList);
			if (ownedLineIntersectionsList == null || ownedLineIntersectionsList.Count == 0)
			{
				if (!opts.RemoveFloatingArcs)
				{
					GetOrAddNode(polyline[0], out var nodeFid);
					GetOrAddNode(polyline[polyline.Count - 1], out var nodeFid2);
					BuildArcBetweenPoints(polyline, polyline.FirstPointOnPolyline(), polyline.LastPointOnPolyline(), nodeFid, nodeFid2);
				}
				continue;
			}
			int nodeFid3 = -1;
			OwnedLineIntersection ownedLineIntersection = ownedLineIntersectionsList[0];
			if (!opts.RemoveHangingArcs && !ownedLineIntersection.GetPOP().IsStart(opts.NodeSnapToVerticeFraction))
			{
				GetOrAddNode(polyline[0], out var nodeFid4);
				GetOrAddNode(ownedLineIntersection, out var nodeFid5);
				BuildArcBetweenPoints(polyline, polyline.FirstPointOnPolyline(), ownedLineIntersection.GetPOP(), nodeFid4, nodeFid5);
				nodeFid3 = nodeFid5;
			}
			for (int j = 0; j < ownedLineIntersectionsList.Count - 1; j++)
			{
				OwnedLineIntersection ownedLineIntersection2 = ownedLineIntersectionsList[j];
				OwnedLineIntersection ownedLineIntersection3 = ownedLineIntersectionsList[j + 1];
				int startNodeFid = ((nodeFid3 == -1) ? GetOrAddNode(ownedLineIntersection2, out var _) : nodeFid3);
				GetOrAddNode(ownedLineIntersection3, out var nodeFid7);
				BuildArcBetweenPoints(polyline, ownedLineIntersection2.GetPOP(), ownedLineIntersection3.GetPOP(), startNodeFid, nodeFid7);
				nodeFid3 = nodeFid7;
			}
			List<OwnedLineIntersection> ownedLineIntersections2 = ownedLineIntersectionsList;
			OwnedLineIntersection ownedLineIntersection4 = ownedLineIntersections2[ownedLineIntersections2.Count - 1];
			if (!opts.RemoveHangingArcs && !ownedLineIntersection4.GetPOP().IsEnd(polyline.SegmentCount, opts.NodeSnapToVerticeFraction))
			{
				if (nodeFid3 == -1)
				{
					GetOrAddNode(ownedLineIntersection, out nodeFid3);
				}
				GetOrAddNode(polyline[polyline.Count - 1], out var nodeFid8);
				BuildArcBetweenPoints(polyline, ownedLineIntersection4.GetPOP(), polyline.LastPointOnPolyline(), nodeFid3, nodeFid8);
			}
		}
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		new HashSet<int>();
		new HashSet<int>();
		for (int countOffset = nodeConnectivity.Count - 1; countOffset >= 0; countOffset--)
		{
			SimpleNodeConnectivity simpleNodeConnectivity = nodeConnectivity[countOffset];
			if (!simpleNodeConnectivity.DefinitelyNeeded)
			{
				if (simpleNodeConnectivity.FirstArcFid == -1)
				{
					intSetSet.Add(countOffset);
				}
				else if (simpleNodeConnectivity.SecondArcFid != -1 && opts.PostMergeArcs)
				{
					int firstArcFid = simpleNodeConnectivity.FirstArcFid;
					bool firstArcIsStart = simpleNodeConnectivity.FirstArcIsStart;
					int secondArcFid = simpleNodeConnectivity.SecondArcFid;
					bool secondArcIsStart = simpleNodeConnectivity.SecondArcIsStart;
					Arc arc = topo.Arcs[secondArcFid];
					int index = ((arc.StartNode == countOffset) ? arc.EndNode : arc.StartNode);
					SimpleNodeConnectivity nodeConnectivity2 = nodeConnectivity[index];
					if (!nodeConnectivity2.DefinitelyNeeded)
					{
						if (nodeConnectivity2.FirstArcFid == secondArcFid)
						{
							nodeConnectivity2.FirstArcFid = firstArcFid;
							nodeConnectivity2.FirstArcIsStart = firstArcIsStart;
						}
						else if (nodeConnectivity2.SecondArcFid == secondArcFid)
						{
							nodeConnectivity2.SecondArcFid = firstArcFid;
							nodeConnectivity2.SecondArcIsStart = firstArcIsStart;
						}
						if (nodeConnectivity2.FirstArcFid == nodeConnectivity2.SecondArcFid)
						{
							nodeConnectivity2.DefinitelyNeeded = true;
						}
						nodeConnectivity[index] = nodeConnectivity2;
					}
					topo.MergeArcs(countOffset, firstArcFid, secondArcFid, firstArcIsStart, secondArcIsStart);
					intSetSet.Add(countOffset);
					intSet.Add(simpleNodeConnectivity.SecondArcFid);
				}
			}
		}
		topo.RemoveNodes(intSetSet);
		foreach (int orderDescending in intSet.OrderDescending())
		{
			topo.RemoveArc(orderDescending, liveUpdateTopology: false);
		}
		try
		{
			topo.TryFixInvalidStates();
		}
		catch (Exception)
		{
		}
		try
		{
			topo.Complete();
		}
		catch (Exception)
		{
		}
		int BuildArcBetweenPoints(Polyline pl, PointOnPolyline popA, PointOnPolyline popB, int ordinal, int endNodeFid)
		{
			int segmentIndexOffset = popA.SegmentIndex + 1;
			int segmentIndex = popB.SegmentIndex;
			if (popA.Fraction == 1.0 || popA.Fraction >= opts.NodeSnapToVerticeFraction)
			{
				segmentIndexOffset++;
			}
			if (popB.Fraction == 0.0 || popB.Fraction <= 1.0 - opts.NodeSnapToVerticeFraction)
			{
				segmentIndex--;
			}
			List<Point> range = ((segmentIndex >= segmentIndexOffset) ? pl.GetRange(segmentIndexOffset, segmentIndex - segmentIndexOffset + 1) : null);
			if (ordinal == endNodeFid && (range == null || range.Count == 0 || range.Count == 1))
			{
				return -1;
			}
			topo.AddArc(ordinal, endNodeFid, range, out var fid);
			UpdateNodeConnectivity(ordinal, fid, isStart: true);
			UpdateNodeConnectivity(endNodeFid, fid, isStart: false);
			return fid;
		}
		bool GetNearestPointInTolerance(Point pt, out int reference)
		{
			reference = -1;
			Extent extent = Extent.CenteredOn(pt, opts.NodeSnapToNodeDistance * 2.0, opts.NodeSnapToNodeDistance * 2.0);
			List<int> elements = ptsSpatialIndex.GetElements(extent);
			if (elements == null || elements.Count == 0)
			{
				return false;
			}
			for (int k = 0; k < elements.Count; k++)
			{
				int element = elements[k];
				Point point = topo.Nodes[element].Point;
				if (point.DistanceTo(pt) < opts.NodeSnapToNodeDistance)
				{
					reference = element;
					return true;
				}
			}
			return false;
		}
		int GetOrAddNode(Point pt, out int reference)
		{
			if (!GetNearestPointInTolerance(pt, out reference))
			{
				nodeConnectivity.Add(new SimpleNodeConnectivity());
				topo.AddNode(pt, out reference);
				ptsSpatialIndex.Add(pt, reference);
			}
			return reference;
		}
		List<OwnedLineIntersection> GetOrMakePlReverseLookup(int idx)
		{
			if (!reverseLookup.TryGetValue(idx, out var reverseLookup2))
			{
				reverseLookup2 = new List<OwnedLineIntersection>();
				reverseLookup[idx] = reverseLookup2;
			}
			return reverseLookup2;
		}
		void UpdateNodeConnectivity(int index2, int arcIdx, bool isStart)
		{
			SimpleNodeConnectivity nodeConnectivity3 = nodeConnectivity[index2];
			if (nodeConnectivity3.FirstArcFid == -1)
			{
				nodeConnectivity3.FirstArcFid = arcIdx;
				nodeConnectivity3.FirstArcIsStart = isStart;
			}
			else if (nodeConnectivity3.SecondArcFid == -1)
			{
				nodeConnectivity3.SecondArcFid = arcIdx;
				nodeConnectivity3.SecondArcIsStart = isStart;
				if (nodeConnectivity3.FirstArcFid == nodeConnectivity3.SecondArcFid)
				{
					nodeConnectivity3.DefinitelyNeeded = true;
				}
			}
			else
			{
				nodeConnectivity3.DefinitelyNeeded = true;
			}
			nodeConnectivity[index2] = nodeConnectivity3;
		}
	}

	public static void ImportPolylines(this Topology topo, IReadOnlyList<Polyline> lines, TopologyImportOptions opts, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		topo.ImportPolylinesOrPolygons_Tin(lines, opts, reporter);
	}

	public static void ImportPolygons(this Topology topo, IReadOnlyList<Polygon> polygons, TopologyImportOptions opts, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		topo.ImportPolylinesOrPolygons_Tin(polygons, opts, reporter);
	}

	public static TinImportResult ImportPolylinesOrPolygons_Tin(this Topology topo, IReadOnlyList<Polyline> newLines, TopologyImportOptions opts, ProgressReporter reporter = null, IReadOnlyList<object[]> newLinesMetadata = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		bool importingArcMetadata = newLinesMetadata != null && newLinesMetadata.Count > 0 && opts.ImportArcMetadata != TopologyImportOptions.ArcMetadataImportOption.NoImport;
		if (importingArcMetadata && newLinesMetadata.Count != newLines.Count)
		{
			throw new InvalidOperationException("Mismatch in count of arcs and arc meta data to import.");
		}
		object[][] existingMetadata = new object[topo.Arcs.Count][];
		for (int i = 0; i < existingMetadata.Length; i++)
		{
			existingMetadata[i] = topo.ArcMetadata.CloneRow(i);
		}
		IReadOnlyList<Polyline> allArcPolylines = topo.GetAllArcPolylines();
		if (!opts.RemoveCollinearInternalPoints)
		{
			throw new NotSupportedException();
		}
		Polyline[] allLines = new Polyline[newLines.Count + allArcPolylines.Count];
		int existingCt = allArcPolylines.Count;
		for (int j = 0; j < existingCt; j++)
		{
			allLines[j] = allArcPolylines[j];
		}
		for (int k = 0; k < newLines.Count; k++)
		{
			allLines[k + existingCt] = newLines[k];
		}
		Extent extent = Extent.Union((IEnumerable<Polyline>)allLines);
		DelaunayTinFactory def = new DelaunayTinFactory(new TriangulationParameters
		{
			Tolerance = Math.Sqrt(extent.Area) * opts.FractionOfSquareRootAreaTinTolerance,
			ConstrainedEdgeMode = ((opts.ImportArcMetadata == TopologyImportOptions.ArcMetadataImportOption.ImportPrioritizeImported) ? ConstrainedEdgeMode.UseLastConstraining : ConstrainedEdgeMode.UseFirstConstraining)
		});
		for (int l = 0; l < allLines.Length; l++)
		{
			if (allLines[l].IsValidPolyline())
			{
				def.AddBreakline(allLines[l]);
			}
		}
		ProgressReporter reporter2 = reporter.WithProgressModifier(0, 0.5f);
		Tin tin = def.ComputeTIN(reporter2);
		Dictionary<int, int> tinPtToNodeMap = new Dictionary<int, int>();
		HashSet<TrianglePointPair> nodesWithRemainingArcs = new HashSet<TrianglePointPair>();
		HashSet<EdgeDefinition> visitedConstrainedEdgesLeavingNodes = new HashSet<EdgeDefinition>();
		List<ConstrainedEdge> constrainedEdgeScratchSpace = new List<ConstrainedEdge>();
		List<int> fidScratchSpace = new List<int>();
		List<EdgeDefinition> edgeScratchSpace = new List<EdgeDefinition>();
		int nextAvailableNode = 0;
		HashSet<int> arcsAlreadyUsed = new HashSet<int>();
		int count = topo.Arcs.Count;
		int count2 = topo.Nodes.Count;
		HashSet<int> intSet = new HashSet<int>(allLines.Length);
		ProgressReporter progressReporter = reporter.WithProgressModifier(50, 0.15f);
		int allLineLength = allLines.Length;
		for (int m = 0; m < allLineLength; m++)
		{
			progressReporter.ReportProgressFraction((float)m / (float)allLineLength);
			if (intSet.Contains(m) || !def.FindConstrainedEdge(m, checkPerimEdges: false, checkBlineEdges: true, out var pointPair))
			{
				continue;
			}
			EdgeDefinition edgeDefinition = tin.FindEdge(pointPair.Item1, pointPair.Item2);
			if (edgeDefinition.APointIndex == -1 && edgeDefinition.ATriangleIndex == -1)
			{
				continue;
			}
			TrianglePointPair trianglePointPair = FindNearestNodeIndex(new TrianglePointPair(edgeDefinition.APointIndex, edgeDefinition.ATriangleIndex));
			if (TryFindUnvisitedEdge(trianglePointPair, out var ed) && !ed.IsNull())
			{
				MarkEdgeVisitedAndUpdateNode(EdgeDefinition.NullEdgeDefinition(), trianglePointPair, out var edgesAroundPt);
				for (int n = 0; n < edgesAroundPt.Count; n++)
				{
					intSet.Add(edgesAroundPt[n].ConstrainedIndex);
				}
			}
		}
		ProgressReporter progressReporter2 = reporter.WithProgressModifier(65, 0.35f);
		int nodesWithRemainingArcCount = nodesWithRemainingArcs.Count;
		while (nodesWithRemainingArcs.Count != 0)
		{
			nodesWithRemainingArcCount = Math.Max(nodesWithRemainingArcs.Count, nodesWithRemainingArcCount);
			progressReporter2.ReportProgressFraction(1f - (float)nodesWithRemainingArcs.Count / (float)nodesWithRemainingArcCount);
			TrianglePointPair trianglePointPair2 = nodesWithRemainingArcs.First();
			if (!TryFindUnvisitedEdge(trianglePointPair2, out var ed2))
			{
				throw new Exception("Node is supposed to still have remaining attached arcs, but couldn't find one.");
			}
			BuildArc(trianglePointPair2, ed2);
		}
		List<int> removedArcFIDs = new List<int>();
		int addedArcCount = topo.Arcs.Count - count;
		int nextAvailableNode2 = nextAvailableNode;
		int nodeCountDelta = topo.Nodes.Count - count2;
		while (nextAvailableNode2 < topo.Nodes.Count)
		{
			topo.RemoveNode(topo.Nodes.Count - 1, liveUpdateTopology: false);
		}
		for (int countOffset = topo.Arcs.Count - 1; countOffset >= 0; countOffset--)
		{
			if (countOffset < count && !arcsAlreadyUsed.Contains(countOffset))
			{
				removedArcFIDs.Add(countOffset);
				topo.RemoveArc(countOffset, liveUpdateTopology: false);
			}
		}
		return new TinImportResult
		{
			AddedArcCount = addedArcCount,
			NodeCountDelta = nodeCountDelta,
			RemovedArcFIDs = removedArcFIDs
		};
		void BuildArc(TrianglePointPair start, EdgeDefinition direction)
		{
			int orMakeNode = GetOrMakeNode(start.PointIndex);
			MarkEdgeVisitedAndUpdateNode(direction, start, out var edgesAroundPt2);
			List<Point> points = new List<Point>();
			int ordinal = 0;
			List<int> intList = new List<int>();
			TrianglePointPair prevPt = start;
			TrianglePointPair trianglePointPair3 = new TrianglePointPair((direction.APointIndex == start.PointIndex) ? direction.BPointIndex : direction.APointIndex, direction.ATriangleIndex);
			while (!IsNode(trianglePointPair3))
			{
				intList.Add(trianglePointPair3.PointIndex);
				ordinal++;
				if (ordinal == 100000)
				{
					throw new Exception("Find nearest node hit max iteration count.");
				}
				Point point = tin.Points[trianglePointPair3.PointIndex];
				bool bearingSegment2Term = false;
				if (points.Count > 0)
				{
					List<Point> pointsList = points;
					Point point2 = pointsList[pointsList.Count - 1];
					Segment segment;
					if (points.Count != 1)
					{
						List<Point> points2 = points;
						segment = new Segment(points2[points2.Count - 2], point2);
					}
					else
					{
						segment = new Segment(topo.Nodes[orMakeNode].Point, point2);
					}
					Segment segment2 = segment;
					Segment segment3 = new Segment(point2, point);
					bearingSegment2Term = segment2.Bearing == segment3.Bearing;
				}
				if (bearingSegment2Term)
				{
					List<Point> points3 = points;
					points3[points3.Count - 1] = point;
				}
				else
				{
					points.Add(point);
				}
				TrianglePointPair trianglePointPair4 = trianglePointPair3;
				trianglePointPair3 = NextPointOnArc(trianglePointPair3, prevPt);
				prevPt = trianglePointPair4;
			}
			int orMakeNode2 = GetOrMakeNode(trianglePointPair3.PointIndex);
			if (points.Count > 0)
			{
				List<Point> points4 = points;
				Point point3 = points4[points4.Count - 1];
				Segment segment4;
				if (points.Count != 1)
				{
					List<Point> points5 = points;
					segment4 = new Segment(points5[points5.Count - 2], point3);
				}
				else
				{
					segment4 = new Segment(topo.Nodes[orMakeNode].Point, point3);
				}
				Segment segment5 = segment4;
				Segment segment6 = new Segment(point3, topo.Nodes[orMakeNode2].Point);
				if (segment5.Bearing == segment6.Bearing)
				{
					points.RemoveAt(points.Count - 1);
				}
			}
			MarkEdgeVisitedAndUpdateNode(tin.FindEdge(trianglePointPair3.PointIndex, prevPt.PointIndex), trianglePointPair3, out edgesAroundPt2);
			if (points.Count == 0)
			{
				points = null;
			}
			if (!def.IsConstrained(direction.APointIndex, direction.BPointIndex, out var _, out var constraintIdx))
			{
				throw new InvalidOperationException("Adding an arc for an edge that isn't constrained?....");
			}
			bool arcsAlreadyUsedConstraintIdxTerm = arcsAlreadyUsed.Add(constraintIdx);
			bool constraintIdxExistingCtTerm = constraintIdx < existingCt;
			if (arcsAlreadyUsedConstraintIdxTerm & constraintIdxExistingCtTerm)
			{
				topo.UpdateArc(constraintIdx, orMakeNode, orMakeNode2, points);
			}
			else
			{
				topo.AddArc(orMakeNode, orMakeNode2, points, out var fid);
				if (constraintIdxExistingCtTerm)
				{
					topo.ArcMetadata.TrySetRow(fid, existingMetadata[constraintIdx]);
				}
				else if (importingArcMetadata)
				{
					topo.ArcMetadata.TrySetRow(fid, newLinesMetadata[constraintIdx - existingCt]);
				}
			}
		}
		TrianglePointPair FindNearestNodeIndex(TrianglePointPair tpp, bool findLoopsAtFirst = false)
		{
			TrianglePointPair prevPt = TrianglePointPair.Null;
			TrianglePointPair trianglePointPair3 = tpp;
			int triangleCountTinTerm = tin.TriangleCount * 2 + 1;
			int ordinal2 = 0;
			do
			{
				ordinal2++;
				if (ordinal2 == triangleCountTinTerm)
				{
					throw new Exception("Find nearest node hit max iteration count.");
				}
				if (IsNode(trianglePointPair3, findLoopsAtFirst))
				{
					return trianglePointPair3;
				}
				TrianglePointPair trianglePointPair4 = trianglePointPair3;
				trianglePointPair3 = NextPointOnArc(trianglePointPair3, prevPt);
				prevPt = trianglePointPair4;
			}
			while (!trianglePointPair3.Equals(tpp));
			IList<ConstrainedEdge> constrainedEdgesAroundPoint = GetConstrainedEdgesAroundPoint(tpp, constrainedEdgeScratchSpace);
			Point point = allLines[constrainedEdgesAroundPoint[0].ConstrainedIndex][0];
			int t = -1;
			tin.FindTriangleAndNearestPointIndex(point, ref t, out var p);
			return new TrianglePointPair(p, t);
		}
		IList<ConstrainedEdge> GetConstrainedEdgesAroundPoint(TrianglePointPair tpp, List<ConstrainedEdge> scratchSpace = null)
		{
			List<EdgeDefinition> edgesAroundPointCCW = tin.EdgesAroundPointCCW(tpp.PointIndex, tpp.TriangleIndex, edgeScratchSpace, fidScratchSpace);
			scratchSpace.Clear();
			List<ConstrainedEdge> scratchSpaceList = scratchSpace ?? new List<ConstrainedEdge>();
			foreach (EdgeDefinition edgesAroundPointCCW2 in edgesAroundPointCCW)
			{
				if (def.IsConstrained(tpp.PointIndex, (edgesAroundPointCCW2.APointIndex == tpp.PointIndex) ? edgesAroundPointCCW2.BPointIndex : edgesAroundPointCCW2.APointIndex, out var _, out var constraintIdx))
				{
					scratchSpaceList.Add(new ConstrainedEdge(edgesAroundPointCCW2, constraintIdx));
				}
			}
			return scratchSpaceList;
		}
		int GetOrMakeNode(int ptIdx)
		{
			if (!tinPtToNodeMap.TryGetValue(ptIdx, out var nextAvailableNode3))
			{
				Point point = tin.Points[ptIdx];
				if (nextAvailableNode < topo.Nodes.Count)
				{
					topo.Nodes[nextAvailableNode].Point = point;
					nextAvailableNode3 = nextAvailableNode;
				}
				else
				{
					topo.AddNode(point, out nextAvailableNode3);
				}
				tinPtToNodeMap.Add(ptIdx, nextAvailableNode3);
				nextAvailableNode++;
			}
			return nextAvailableNode3;
		}
		bool IsNode(TrianglePointPair tpp, bool isStartNode = false)
		{
			if (tinPtToNodeMap.ContainsKey(tpp.PointIndex))
			{
				return true;
			}
			IList<ConstrainedEdge> constrainedEdgesAroundPointList = GetConstrainedEdgesAroundPoint(tpp, constrainedEdgeScratchSpace);
			if (constrainedEdgesAroundPointList.Count == 1 || constrainedEdgesAroundPointList.Count > 2)
			{
				return true;
			}
			if (constrainedEdgesAroundPointList.Count == 2)
			{
				int constrainedIndex = constrainedEdgesAroundPointList[0].ConstrainedIndex;
				int constrainedIndex2 = constrainedEdgesAroundPointList[1].ConstrainedIndex;
				if (constrainedIndex != constrainedIndex2)
				{
					return true;
				}
				if (isStartNode && allLines[constrainedIndex].Contains(tin.Points[tpp.PointIndex]))
				{
					return true;
				}
			}
			return false;
		}
		void MarkEdgeVisitedAndUpdateNode(EdgeDefinition edgeDefinition2, TrianglePointPair pt, out IList<ConstrainedEdge> reference)
		{
			if (!edgeDefinition2.IsNull())
			{
				visitedConstrainedEdgesLeavingNodes.Add(edgeDefinition2);
			}
			IList<ConstrainedEdge> referenceList = GetConstrainedEdgesAroundPoint(pt, constrainedEdgeScratchSpace);
			int ordinal3 = 0;
			int count3 = referenceList.Count;
			for (int intervalIndex = 0; intervalIndex < count3; intervalIndex++)
			{
				if (!visitedConstrainedEdgesLeavingNodes.Contains(referenceList[intervalIndex].Edge))
				{
					ordinal3++;
				}
			}
			bool nodesWithRemainingArcsPtTerm = nodesWithRemainingArcs.Contains(pt);
			if (!nodesWithRemainingArcsPtTerm && ordinal3 > 0)
			{
				nodesWithRemainingArcs.Add(pt);
			}
			else if (nodesWithRemainingArcsPtTerm && ordinal3 == 0)
			{
				nodesWithRemainingArcs.Remove(pt);
			}
			reference = referenceList;
		}
		TrianglePointPair NextPointOnArc(TrianglePointPair curPt, TrianglePointPair prevPt)
		{
			IList<ConstrainedEdge> constrainedEdgesAroundPoint2 = GetConstrainedEdgesAroundPoint(curPt, constrainedEdgeScratchSpace);
			EdgeDefinition edge = constrainedEdgesAroundPoint2[0].Edge;
			int bPointIndex = ((edge.APointIndex == curPt.PointIndex) ? edge.BPointIndex : edge.APointIndex);
			int aTriangleIndex = edge.ATriangleIndex;
			if (bPointIndex == prevPt.PointIndex && !prevPt.IsNull())
			{
				edge = constrainedEdgesAroundPoint2[1].Edge;
				bPointIndex = ((edge.APointIndex == curPt.PointIndex) ? edge.BPointIndex : edge.APointIndex);
				aTriangleIndex = edge.ATriangleIndex;
			}
			return new TrianglePointPair(bPointIndex, aTriangleIndex);
		}
		bool TryFindUnvisitedEdge(TrianglePointPair tpp, out EdgeDefinition reference)
		{
			IList<ConstrainedEdge> constrainedEdgesAroundPoint3 = GetConstrainedEdgesAroundPoint(tpp, constrainedEdgeScratchSpace);
			for (int intervalIndex2 = 0; intervalIndex2 < constrainedEdgesAroundPoint3.Count; intervalIndex2++)
			{
				EdgeDefinition edge = constrainedEdgesAroundPoint3[intervalIndex2].Edge;
				if (!visitedConstrainedEdgesLeavingNodes.Contains(edge))
				{
					reference = edge;
					return true;
				}
			}
			reference = EdgeDefinition.NullEdgeDefinition();
			return false;
		}
	}
}
