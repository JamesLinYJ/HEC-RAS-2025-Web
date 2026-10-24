using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public class DeleteNodesOperation : TopologyEditOperation
{
	private IList<int> _nodeIndices;

	private bool _limitToOneMerge;

	public override string Description => "Delete node";

	public DeleteNodesOperation(TopologyEditContext context, IList<int> nodeIndices, bool limitToOneMerge = false)
		: base(context, null)
	{
		_nodeIndices = new List<int>(nodeIndices);
		_limitToOneMerge = limitToOneMerge;
	}

	protected override bool ApplyInternal()
	{
		ClearAllSelection();
		_deletedNodes.Clear();
		TryDeleteNodes();
		return true;
	}

	private void TryDeleteNodes()
	{
		TryDeleteNodes(_nodeIndices, _context, in _deletedNodes, in _deletedArcs, combineArcsIfOnlyTwoConnectToNode: true, _limitToOneMerge);
	}

	public static bool TryDeleteNodes(IEnumerable<int> nodeIndices, TopologyEditContext context, in Stack<SavedNode> deletedNodes, in Stack<SavedArc> deletedArcs, bool combineArcsIfOnlyTwoConnectToNode = true, bool limitToOneMerge = false)
	{
		return TryDeleteNodes(nodeIndices, context.Topology, in deletedNodes, in deletedArcs, combineArcsIfOnlyTwoConnectToNode, limitToOneMerge, context.OnNodeRemoved, context.OnArcRemoved);
	}

	public static bool TryDeleteNodes(IEnumerable<int> nodeIndices, Topology topo, in Stack<SavedNode> deletedNodes, in Stack<SavedArc> deletedArcs, bool combineArcsIfOnlyTwoConnectToNode = true, bool limitToOneMerge = false, Action<int> onNodeRemoved = null, Action<int> onArcRemoved = null)
	{
		bool condition = false;
		foreach (int orderByDescending in nodeIndices.OrderByDescending((int i) => i))
		{
			bool combineArcsIfOnlyTwoConnectToNode2 = combineArcsIfOnlyTwoConnectToNode && (!limitToOneMerge || !condition);
			TryDeleteNode(orderByDescending, topo, in deletedNodes, in deletedArcs, combineArcsIfOnlyTwoConnectToNode2, out var mergedTwoArcs, onNodeRemoved, onArcRemoved);
			if (mergedTwoArcs)
			{
				condition = true;
			}
		}
		return condition;
	}

	public static void TryDeleteNode(int fid, TopologyEditContext context, in Stack<SavedNode> deletedNodes, in Stack<SavedArc> deletedArcs, bool combineArcsIfOnlyTwoConnectToNode, out bool mergedTwoArcs)
	{
		TryDeleteNode(fid, context.Topology, in deletedNodes, in deletedArcs, combineArcsIfOnlyTwoConnectToNode, out mergedTwoArcs, context.OnNodeRemoved, context.OnArcRemoved);
	}

	public static void TryDeleteNode(int fid, Topology topo, in Stack<SavedNode> deletedNodes, in Stack<SavedArc> deletedArcs, bool combineArcsIfOnlyTwoConnectToNode, out bool mergedTwoArcs, Action<int> onNodeRemoved = null, Action<int> onArcRemoved = null)
	{
		mergedTwoArcs = false;
		if (fid == -1)
		{
			return;
		}
		Node node = topo.Nodes[fid];
		if (node.Arcs.Count > 0)
		{
			if (node.Arcs.Count == 2 && combineArcsIfOnlyTwoConnectToNode)
			{
				TryMergeArcs(fid, node, topo, in deletedNodes, in deletedArcs, onNodeRemoved, onArcRemoved);
				mergedTwoArcs = true;
			}
		}
		else
		{
			SavedNode savedNode = new SavedNode(fid, node);
			topo.RemoveNode(fid);
			onNodeRemoved?.Invoke(fid);
			deletedNodes.Push(savedNode);
		}
	}

	private static void TryMergeArcs(int nodeFid, Node node, Topology topo, in Stack<SavedNode> deletedNodes, in Stack<SavedArc> deletedArcs, Action<int> onNodeRemoved, Action<int> onArcRemoved)
	{
		NodeArcConnection nodeArcConnection = node.Arcs[0];
		NodeArcConnection nodeArcConnection2 = node.Arcs[1];
		int arcID = nodeArcConnection.ArcID;
		int arcID2 = nodeArcConnection2.ArcID;
		if (arcID != arcID2)
		{
			Arc arc = topo.Arcs[arcID];
			int survivedArcCount = arc.InternalPoints?.Count ?? 0;
			object[] metaDataRow = topo.ArcMetadata.CloneRow(arcID);
			topo.MergeArcs(nodeFid);
			int count = deletedNodes.Count;
			DeleteArcsOperation.TryDeleteArc(arcID2, topo, in deletedArcs, in deletedNodes, (Stack<SavedControlPoint>)null, tryDeleteEndingNodes: true, tryDeletingDupedControlPts: false, onNodeRemoved, onArcRemoved);
			if (deletedNodes.Count != count + 1)
			{
				throw new InvalidOperationException("The DeleteArcOperation should have only deleted one node, the node that WE are currently deleting.");
			}
			SavedNode pop = deletedNodes.Pop();
			pop.SurvivedArc = new SavedArc((arcID2 < arcID) ? (arcID - 1) : arcID, arc, metaDataRow);
			pop.SurvivedArcCount = survivedArcCount;
			pop.SurvivedArcReconnectedStart = nodeArcConnection.IsStart;
			deletedNodes.Push(pop);
		}
	}

	protected override bool UndoInternal()
	{
		UndoDeleteNodes(_deletedNodes, _context);
		DeleteArcsOperation.UndoDeleteArcs(_deletedArcs, null, _context);
		UndoSelectionState();
		return true;
	}

	public static void UndoDeleteNodes(Stack<SavedNode> deletedNodes, TopologyEditContext context)
	{
		while (deletedNodes.Count > 0)
		{
			UndoDeleteNode(deletedNodes.Pop(), context);
		}
	}

	private static void UndoDeleteNode(SavedNode node, TopologyEditContext context)
	{
		context.Topology.InsertNode(node.FID, node.OriginalNode.Point);
		context.OnNodeInserted(node.FID);
		if (!node.SurvivedArc.IsNull())
		{
			Arc originalArc = node.SurvivedArc.OriginalArc;
			if (node.SurvivedArcReconnectedStart)
			{
				List<Point> range = originalArc.InternalPoints.GetRange(originalArc.InternalPointCount - node.SurvivedArcCount, node.SurvivedArcCount);
				originalArc.SetPoints(range);
			}
			else
			{
				List<Point> range2 = originalArc.InternalPoints.GetRange(0, node.SurvivedArcCount);
				originalArc.SetPoints(range2);
			}
			context.Topology.ReconnectArc(node.SurvivedArc.FID, node.FID, node.SurvivedArcReconnectedStart);
			context.Topology.ArcMetadata.TrySetRow(node.SurvivedArc.FID, node.SurvivedArc.MetaDataRow);
		}
	}
}
