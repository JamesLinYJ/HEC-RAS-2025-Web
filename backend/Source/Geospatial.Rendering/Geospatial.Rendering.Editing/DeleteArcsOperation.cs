using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public class DeleteArcsOperation : TopologyEditOperation
{
	private IList<int> _arcIndices;

	public override string Description => "Delete Arcs";

	public DeleteArcsOperation(TopologyEditContext context, IList<int> arcIndices)
		: base(context, null)
	{
		_arcIndices = new List<int>(arcIndices);
	}

	protected override bool ApplyInternal()
	{
		ClearAllSelection();
		_deletedArcs.Clear();
		_deletedControlPoints.Clear();
		DeleteArcs();
		return true;
	}

	private void DeleteArcs()
	{
		DeleteArcs(_arcIndices, _context, in _deletedArcs, in _deletedNodes, in _deletedControlPoints);
	}

	public static void DeleteArcs(IEnumerable<int> arcIndices, TopologyEditContext context, in Stack<SavedArc> deletedArcs, in Stack<SavedNode> deletedNodes, in Stack<SavedControlPoint> deletedControlPoints, bool tryDeleteEndingNodes = true, bool tryDeletingDupedControlPoints = true)
	{
		if (arcIndices.Count() == 0)
		{
			return;
		}
		Topology topology = context.Topology;
		List<int> intList = new List<int>();
		foreach (int arcIndex in arcIndices)
		{
			Arc arc = topology.Arcs[arcIndex];
			if (!intList.Contains(arc.StartNode))
			{
				intList.Add(arc.StartNode);
			}
			if (!intList.Contains(arc.EndNode))
			{
				intList.Add(arc.EndNode);
			}
		}
		foreach (int orderByDescending in arcIndices.OrderByDescending((int i) => i))
		{
			TryDeleteArc(orderByDescending, context, in deletedArcs, in deletedNodes, in deletedControlPoints, tryDeleteEndingNodes: false, tryDeletingDupedControlPoints);
		}
		if (!tryDeleteEndingNodes)
		{
			return;
		}
		foreach (int orderByDescending2 in intList.OrderByDescending((int node) => node))
		{
			if (orderByDescending2 != -1)
			{
				DeleteNodesOperation.TryDeleteNode(orderByDescending2, context, in deletedNodes, (Stack<SavedArc>)null, combineArcsIfOnlyTwoConnectToNode: false, out var _);
			}
		}
	}

	public static void TryDeleteArc(int fid, TopologyEditContext context, in Stack<SavedArc> deletedArcs, in Stack<SavedNode> deletedNodes, in Stack<SavedControlPoint> deletedControlPoints, bool tryDeleteEndingNodes = true, bool tryDeletingDupedControlPts = true)
	{
		TryDeleteArc(fid, context.Topology, in deletedArcs, in deletedNodes, in deletedControlPoints, tryDeleteEndingNodes, tryDeletingDupedControlPts, context.OnNodeRemoved, context.OnArcRemoved, context);
	}

	public static void TryDeleteArc(int fid, Topology topo, in Stack<SavedArc> deletedArcs, in Stack<SavedNode> deletedNodes, in Stack<SavedControlPoint> deletedControlPoints, bool tryDeleteEndingNodes = true, bool tryDeletingDupedControlPts = false, Action<int> onNodeRemoved = null, Action<int> onArcRemoved = null, TopologyEditContext contextForControlPoints = null)
	{
		Arc arc = topo.Arcs[fid];
		object[] metaDataRow = topo.ArcMetadata.CloneRow(fid);
		int startNode = arc.StartNode;
		int endNode = arc.EndNode;
		int startNode2 = ((startNode < endNode) ? startNode : endNode);
		int startNode3 = ((startNode > endNode) ? startNode : endNode);
		topo.RemoveArc(fid);
		onArcRemoved?.Invoke(fid);
		deletedArcs.Push(new SavedArc(fid, arc, metaDataRow));
		if (tryDeleteEndingNodes)
		{
			DeleteNodesOperation.TryDeleteNode(startNode3, topo, in deletedNodes, (Stack<SavedArc>)null, combineArcsIfOnlyTwoConnectToNode: false, out var mergedTwoArcs, onNodeRemoved, onArcRemoved);
			if (startNode3 != startNode2)
			{
				DeleteNodesOperation.TryDeleteNode(startNode2, topo, in deletedNodes, (Stack<SavedArc>)null, combineArcsIfOnlyTwoConnectToNode: false, out mergedTwoArcs, onNodeRemoved, onArcRemoved);
			}
		}
		if (tryDeletingDupedControlPts)
		{
			TryDeleteDupedControlPoints(topo, fid, arc, in deletedControlPoints, contextForControlPoints);
		}
	}

	private static void TryDeleteDupedControlPoints(Topology topo, int fid, Arc arc, in Stack<SavedControlPoint> deletedControlPoints, TopologyEditContext contextForControlPoints)
	{
		if (contextForControlPoints == null)
		{
			throw new InvalidOperationException("Control point deletion requires TopologyEditContext. Pass contextForControlPoints or set tryDeletingDupedControlPts=false.");
		}
		if (arc.RegionA == arc.RegionB)
		{
			return;
		}
		int unity = -1;
		if (IsArcInInnerRing(fid, arc.RegionA, topo))
		{
			unity = arc.RegionB;
		}
		else if (IsArcInInnerRing(fid, arc.RegionB, topo))
		{
			unity = arc.RegionA;
		}
		if (unity >= 0)
		{
			Region region = topo.CompletedRegions[unity];
			if (region.ControlPointIndices.Count > 0)
			{
				DeleteControlPointsOperation.DeleteControlPoint(contextForControlPoints, region.ControlPointIndex, in deletedControlPoints);
			}
			return;
		}
		int regionB = ((arc.RegionB != -1) ? arc.RegionB : arc.RegionA);
		if (regionB >= 0)
		{
			Region region2 = topo.CompletedRegions[regionB];
			if (region2.ControlPointIndices.Count != 0)
			{
				DeleteControlPointsOperation.DeleteControlPoint(contextForControlPoints, region2.ControlPointIndex, in deletedControlPoints);
			}
		}
	}

	private static bool IsArcInInnerRing(int arcFid, int regionIndex, Topology topo)
	{
		if (regionIndex < 0)
		{
			return false;
		}
		foreach (RegionRing innerRing in topo.CompletedRegions[regionIndex].InnerRings)
		{
			if (innerRing.ContainsArc(arcFid))
			{
				return true;
			}
		}
		return false;
	}

	private static void UndoDeleteArc(SavedArc savedArc, TopologyEditContext context)
	{
		context.Topology.InsertArc(savedArc.FID, savedArc.OriginalArc);
		context.Topology.ArcMetadata.TrySetRow(savedArc.FID, savedArc.MetaDataRow);
		context.OnArcInserted(savedArc.FID);
	}

	public void UndoDeleteArcs()
	{
		UndoDeleteArcs(_deletedArcs, _deletedControlPoints, _context);
	}

	public static void UndoDeleteArcs(Stack<SavedArc> deletedArcs, Stack<SavedControlPoint> deletedControlPoints, TopologyEditContext context)
	{
		DeleteControlPointsOperation.UndoDeleteControlPoints(context, deletedControlPoints);
		while (deletedArcs.Count > 0)
		{
			UndoDeleteArc(deletedArcs.Pop(), context);
		}
	}

	protected override bool UndoInternal()
	{
		DeleteNodesOperation.UndoDeleteNodes(_deletedNodes, _context);
		UndoDeleteArcs();
		UndoSelectionState();
		return true;
	}
}
