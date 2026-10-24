using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public static class NodeArcEditGeometry
{
	public static bool CanMoveNodes(BaseTopology network, Vector deltaOffset, IEnumerable<int> nodeIndices, double cellSize)
	{
		foreach (int nodeIndex in nodeIndices)
		{
			Point pt = network.Nodes[nodeIndex].Point.Translate(deltaOffset);
			foreach (NodeArcConnection arc in network.Nodes[nodeIndex].Arcs)
			{
				BaseArc baseArc = network.Arcs[arc.ArcID];
				int internalPointCount = baseArc.InternalPointCount;
				if (internalPointCount > 1)
				{
					Point pt2 = (arc.IsStart ? baseArc.InternalPoints[1] : baseArc.InternalPoints[internalPointCount - 2]);
					if (EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(pt, pt2, cellSize))
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	public static bool CanMergeNodes(BaseTopology network, int removeNodeIdx, int survivingNodeIdx)
	{
		foreach (NodeArcConnection arc in network.Nodes[removeNodeIdx].Arcs)
		{
			BaseArc baseArc = network.Arcs[arc.ArcID];
			switch (baseArc.InternalPointCount)
			{
			case 1:
				if (((baseArc.StartNode == removeNodeIdx) ? baseArc.EndNode : baseArc.StartNode) == survivingNodeIdx)
				{
					return false;
				}
				break;
			case 0:
				foreach (NodeArcConnection arc2 in network.Nodes[survivingNodeIdx].Arcs)
				{
					BaseArc baseArc2 = network.Arcs[arc2.ArcID];
					if (baseArc2.InternalPointCount <= 0)
					{
						int startNode = baseArc.StartNode;
						int endNode = baseArc.EndNode;
						if (arc.IsStart)
						{
							startNode = survivingNodeIdx;
						}
						else
						{
							endNode = survivingNodeIdx;
						}
						if (baseArc2.StartNode == startNode && baseArc2.EndNode == endNode)
						{
							return false;
						}
						if (baseArc2.StartNode == endNode && baseArc2.EndNode == startNode)
						{
							return false;
						}
					}
				}
				break;
			}
		}
		return true;
	}

	public static bool CanMergeNodeIntoArc(BaseTopology network, Point point, int nodeIndex, int hitArcIndex)
	{
		BaseArc baseArc = network.Arcs[hitArcIndex];
		int startNode = baseArc.StartNode;
		int endNode = baseArc.EndNode;
		if (startNode == nodeIndex || endNode == nodeIndex)
		{
			if (baseArc.InternalPointCount <= 1)
			{
				return false;
			}
			bool startNodeNodeIndexTerm = startNode == nodeIndex;
			Polyline arcPolyline = network.GetArcPolyline(hitArcIndex);
			PointOnPolyline pointOnPolyline = arcPolyline.NearestPointOnPolyline(point);
			if (startNodeNodeIndexTerm && pointOnPolyline.SegmentIndex <= 1)
			{
				return false;
			}
			if (!startNodeNodeIndexTerm && pointOnPolyline.SegmentIndex >= arcPolyline.SegmentCount - 2)
			{
				return false;
			}
		}
		else
		{
			bool nodesFormArc = network.NodesFormArc(nodeIndex, startNode, out var sharedArcIndex);
			bool nodesFormArc2 = network.NodesFormArc(nodeIndex, endNode, out var sharedArcIndex2);
			if (nodesFormArc && network.Arcs[sharedArcIndex].InternalPointCount == 0 && network.GetArcPolyline(hitArcIndex).NearestPointOnPolyline(point).SegmentIndex == 0)
			{
				return false;
			}
			if (nodesFormArc2 && network.Arcs[sharedArcIndex2].InternalPointCount == 0)
			{
				Polyline arcPolyline2 = network.GetArcPolyline(hitArcIndex);
				if (arcPolyline2.NearestPointOnPolyline(point).SegmentIndex == arcPolyline2.SegmentCount - 1)
				{
					return false;
				}
			}
		}
		return true;
	}

	public static bool CanFinishAddingAtNode(BaseTopology network, int arcFid, int nodeIndex)
	{
		BaseArc baseArc = network.Arcs[arcFid];
		if (baseArc.StartNode != nodeIndex)
		{
			return true;
		}
		if (baseArc.InternalPointCount <= 1)
		{
			return false;
		}
		if (baseArc.InternalPoints[0] == baseArc.InternalPoints[baseArc.InternalPointCount - 1])
		{
			return false;
		}
		return true;
	}

	public static bool IsCollapsedLoopArc(BaseTopology network, int arcFid)
	{
		BaseArc baseArc = network.Arcs[arcFid];
		if (baseArc.EndNode != -1 && baseArc.StartNode == baseArc.EndNode)
		{
			return baseArc.InternalPointCount <= 1;
		}
		return false;
	}

	public static bool CanSplitArcAtPoint(BaseTopology network, int addingArcFid, Point point, int splitArcIndex)
	{
		BaseArc baseArc = network.Arcs[addingArcFid];
		BaseArc baseArc2 = network.Arcs[splitArcIndex];
		int startNode = baseArc.StartNode;
		PointOnPolyline pointOnPolyline = network.GetArcPolyline(splitArcIndex).NearestPointOnPolyline(point);
		int internalPointCount = baseArc2.InternalPointCount;
		if (baseArc.InternalPointCount > 0)
		{
			return true;
		}
		int startNode2 = baseArc2.StartNode;
		int endNode = baseArc2.EndNode;
		if ((endNode == startNode || startNode2 == startNode) && internalPointCount == 0)
		{
			return false;
		}
		if (endNode == startNode && (pointOnPolyline.SegmentIndex == internalPointCount || (pointOnPolyline.SegmentIndex == internalPointCount - 1 && pointOnPolyline.Fraction == 1.0)))
		{
			return false;
		}
		if (startNode2 == startNode && (pointOnPolyline.SegmentIndex == 0 || (pointOnPolyline.SegmentIndex == 1 && pointOnPolyline.Fraction == 0.0)))
		{
			return false;
		}
		return true;
	}

	public static IReadOnlyList<Segment> GetNodeAttachedSegments(BaseTopology network, int nodeIndex)
	{
		CompactList<Segment> compactList = default;
		foreach (NodeArcConnection arc in network.Nodes[nodeIndex].Arcs)
		{
			Polyline arcPolyline = network.GetArcPolyline(arc.ArcID);
			Segment segment;
			if (!arc.IsStart)
			{
				IReadOnlyList<Segment> segments = arcPolyline.Segments;
				segment = segments[segments.Count - 1].Reverse();
			}
			else
			{
				segment = arcPolyline.Segments[0];
			}
			Segment segment2 = segment;
			compactList.Add(segment2);
		}
		return compactList;
	}
}
