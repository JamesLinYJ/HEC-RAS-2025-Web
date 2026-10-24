using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class TranslateSelectedNodesOperation : TopologyEditOperation
{
	private Vector _delta;

	private CompactList<int> _translatedNodes;

	private CompactList<SavedNode> _combinedNodes;

	private Dictionary<int, NodeArcConnection> _nodesMergedWithEndPoint = new Dictionary<int, NodeArcConnection>();

	private bool _combinedNodeWithArc;

	private int _addedNodeFID;

	private CompactList<SplitArcOperation.SplitArcResult> _splitArcs;

	public override string Description => "Move Node";

	public TranslateSelectedNodesOperation(TopologyEditContext context, HitTestArgs location, Vector delta)
		: base(context, location)
	{
		_delta = delta;
	}

	protected override bool ApplyInternal()
	{
		TranslateOrCombineSelection();
		return true;
	}

	protected override bool UndoInternal()
	{
		UndoTranslateOrCombineSelection();
		UndoSelectionState();
		return true;
	}

	private void TranslateOrCombineSelection()
	{
		_deletedNodes.Clear();
		_translatedNodes.Clear();
		_combinedNodes.Clear();
		if (_previouslySelectedNodes.Count == 1)
		{
			int previouslySelectedNodesTerm = _previouslySelectedNodes.First();
			if ((NetworkRenderLayer.HitTestNode(_context.Topology, _location, out var elementIndex) && elementIndex != previouslySelectedNodesTerm && MergeNodeIntoNode(previouslySelectedNodesTerm, elementIndex)) || (NetworkRenderLayer.HitTestArcs(_context.Topology, _location, out var _, out var hitArcs) && MergeNodeIntoArc(previouslySelectedNodesTerm, hitArcs)))
			{
				return;
			}
		}
		foreach (int element2 in _previouslySelectedNodes.Elements)
		{
			TranslateNode(element2);
			_translatedNodes.Add(element2);
		}
	}

	private void UndoTranslateOrCombineSelection()
	{
		if (_combinedNodeWithArc)
		{
			UndoCombineArc();
			return;
		}
		foreach (int translatedNode in _translatedNodes)
		{
			UndoTranslateNode(translatedNode);
		}
		foreach (SavedNode combinedNode in _combinedNodes)
		{
			UndoCombineNode(combinedNode);
		}
	}

	private void TranslateNode(int nodeIdx)
	{
		Point point = _context.Topology.Nodes[nodeIdx].Point.Translate(_delta);
		foreach (NodeArcConnection arc2 in _context.Topology.Nodes[nodeIdx].Arcs)
		{
			Arc arc = _context.Topology.Arcs[arc2.ArcID];
			if (arc.InternalPointCount == 0)
			{
				continue;
			}
			if (arc2.IsStart && EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(arc.InternalPoints[0], point, _location.Window.CellSize))
			{
				arc.InternalPoints.RemoveAt(0);
				_nodesMergedWithEndPoint[nodeIdx] = arc2;
			}
			else if (!arc2.IsStart)
			{
				PointCollection internalPoints = arc.InternalPoints;
				if (EditableVectorHelpers.IsHitTestWithinDuplicatePointTolerance(internalPoints[internalPoints.Count - 1], point, _location.Window.CellSize))
				{
					arc.InternalPoints.RemoveAt(arc.InternalPointCount - 1);
					_nodesMergedWithEndPoint[nodeIdx] = arc2;
				}
			}
		}
		_context.Topology.MoveNode(nodeIdx, point);
	}

	private void UndoTranslateNode(int nodeIdx)
	{
		Node node = _context.Topology.Nodes[nodeIdx];
		if (_nodesMergedWithEndPoint.TryGetValue(nodeIdx, out var localValue))
		{
			Arc arc = _context.Topology.Arcs[localValue.ArcID];
			if (localValue.IsStart)
			{
				arc.InternalPoints.Insert(0, node.Point);
			}
			else
			{
				arc.InternalPoints.Add(node.Point);
			}
			_nodesMergedWithEndPoint.Remove(nodeIdx);
		}
		_context.Topology.MoveNode(nodeIdx, node.Point.Translate(_delta.Negate()));
	}

	private bool MergeNodeIntoNode(int removeNodeIdx, int survivingNodeIdx)
	{
		Node node = _context.Topology.Nodes[removeNodeIdx];
		Node node2 = _context.Topology.Nodes[survivingNodeIdx];
		SavedNode savedNode = new SavedNode(removeNodeIdx, new Node(node.Point)
		{
			Arcs = new List<NodeArcConnection>(node.Arcs)
		});
		if (!EditableTopology.CanMergeNodes(_context.Topology, removeNodeIdx, survivingNodeIdx))
		{
			return false;
		}
		int intervalIndex;
		for (intervalIndex = 0; intervalIndex < node.Arcs.Count; intervalIndex++)
		{
			NodeArcConnection nodeArcConnection = node.Arcs[intervalIndex];
			_context.Topology.ReconnectArc(nodeArcConnection.ArcID, survivingNodeIdx, nodeArcConnection.IsStart);
			intervalIndex--;
		}
		CompactList<int> compactList = default;
		foreach (int distinctBy in (from item in node2.Arcs
			select item.ArcID into item
			orderby item descending
			select item).DistinctBy((int ordinal) => ordinal).ToList())
		{
			Arc arc = _context.Topology.Arcs[distinctBy];
			if (arc.InternalPointCount == 0 && arc.StartNode == arc.EndNode)
			{
				compactList.Add(arc.StartNode);
				DeleteArcsOperation.TryDeleteArc(distinctBy, _context, in _deletedArcs, in _deletedNodes, in _deletedControlPoints, tryDeleteEndingNodes: false, tryDeletingDupedControlPts: false);
			}
		}
		int count = _deletedNodes.Count;
		compactList.Add(removeNodeIdx);
		DeleteNodesOperation.TryDeleteNodes(compactList, _context, in _deletedNodes, (Stack<SavedArc>)null, combineArcsIfOnlyTwoConnectToNode: false);
		_combinedNodes.Add(savedNode);
		if (_deletedNodes.Count < count + 1)
		{
			throw new InvalidOperationException("The node did not delete properly when combining. This is an error.");
		}
		return true;
	}

	private bool MergeNodeIntoArc(int nodeIdx, IList<FeatureSegmentSubset> hitArcs)
	{
		foreach (FeatureSegmentSubset hitArc in hitArcs)
		{
			if (!EditableTopology.CanMergeNodeIntoArc(_context.Topology, _location.QueryPoint, nodeIdx, hitArc.Fid))
			{
				return false;
			}
		}
		if (!SplitArcOperation.SplitArc(_context.Topology, _location, hitArcs, _context.AllowOverlappingArcSplits, out _addedNodeFID, out _splitArcs))
		{
			return false;
		}
		_context.OnNodeAdded(_addedNodeFID);
		SplitArcOperation.NotifyArcsAdded(_context, _splitArcs);
		MergeNodeIntoNode(nodeIdx, _addedNodeFID);
		_combinedNodeWithArc = true;
		return true;
	}

	private bool UndoCombineArc()
	{
		UndoCombineNode(_combinedNodes[0]);
		SplitArcOperation.UnsplitArcs(_context.Topology, _splitArcs, _addedNodeFID, _context.OnArcRemoved);
		_context.OnNodeRemoved(_addedNodeFID);
		return true;
	}

	private void UndoCombineNode(SavedNode combinedNode)
	{
		DeleteNodesOperation.UndoDeleteNodes(_deletedNodes, _context);
		DeleteArcsOperation.UndoDeleteArcs(_deletedArcs, _deletedControlPoints, _context);
		for (int i = 0; i < combinedNode.OriginalNode.Arcs.Count; i++)
		{
			NodeArcConnection nodeArcConnection = combinedNode.OriginalNode.Arcs[i];
			_context.Topology.ReconnectArc(nodeArcConnection.ArcID, combinedNode.FID, nodeArcConnection.IsStart);
		}
	}
}
