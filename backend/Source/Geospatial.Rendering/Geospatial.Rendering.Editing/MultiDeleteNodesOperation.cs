using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public class MultiDeleteNodesOperation : TopologyEditOperation
{
	private readonly List<int> _nodeIndices;

	private readonly List<DeleteNodesOperation> _subOperations = new List<DeleteNodesOperation>();

	public override string Description => "Delete nodes";

	public MultiDeleteNodesOperation(TopologyEditContext context, IList<int> nodeIndices)
		: base(context, null)
	{
		_nodeIndices = new List<int>(nodeIndices);
	}

	protected override bool ApplyInternal()
	{
		ClearAllSelection();
		DeleteNodes(_context.Topology, _nodeIndices, _context, out var subOperationsOut);
		_subOperations.Clear();
		_subOperations.AddRange(subOperationsOut);
		return true;
	}

	protected override bool UndoInternal()
	{
		for (int countOffset = _subOperations.Count - 1; countOffset >= 0; countOffset--)
		{
			_subOperations[countOffset].Undo();
		}
		UndoSelectionState();
		return true;
	}

	public static int DeleteNodes(Topology topo, IEnumerable<int> nodeIndices, TopologyEditContext context, out List<DeleteNodesOperation> subOperationsOut, Action<int> onNodeRemoved = null, Action<int> onArcRemoved = null)
	{
		subOperationsOut = new List<DeleteNodesOperation>();
		HashSet<int> nodeIndicesSet = new HashSet<int>(nodeIndices);
		int ordinal = 0;
		while (nodeIndicesSet.Count > 0)
		{
			int count = topo.Nodes.Count;
			HashSet<int> intSet = new HashSet<int>();
			if (context != null)
			{
				DeleteNodesOperation deleteNodesOperation = new DeleteNodesOperation(context, nodeIndicesSet.ToList(), limitToOneMerge: true);
				deleteNodesOperation.Apply();
				subOperationsOut.Add(deleteNodesOperation);
				foreach (SavedNode deletedNode in deleteNodesOperation.DeletedNodes)
				{
					intSet.Add(deletedNode.FID);
				}
			}
			else
			{
				Stack<SavedNode> deletedNodes = new Stack<SavedNode>();
				Stack<SavedArc> deletedArcs = new Stack<SavedArc>();
				DeleteNodesOperation.TryDeleteNodes(nodeIndicesSet.ToList(), topo, in deletedNodes, in deletedArcs, combineArcsIfOnlyTwoConnectToNode: true, limitToOneMerge: true, onNodeRemoved, onArcRemoved);
				foreach (SavedNode deletedNode2 in deletedNodes)
				{
					intSet.Add(deletedNode2.FID);
				}
			}
			ordinal += intSet.Count;
			HashSet<int> nodeIndicesSetSet = new HashSet<int>();
			foreach (int fid in nodeIndicesSet)
			{
				if (!intSet.Contains(fid))
				{
					int deletedFidTerm = intSet.Count((int deleted) => deleted < fid);
					nodeIndicesSetSet.Add(fid - deletedFidTerm);
				}
			}
			nodeIndicesSet = nodeIndicesSetSet;
			if (topo.Nodes.Count == count)
			{
				break;
			}
		}
		return ordinal;
	}

	public static int MergeCollinearNodes(Topology topo)
	{
		List<int> intList = new List<int>();
		for (int i = 0; i < topo.Nodes.Count; i++)
		{
			if (topo.Nodes[i].Arcs.Count == 2)
			{
				intList.Add(i);
			}
		}
		if (intList.Count == 0)
		{
			return 0;
		}
		List<DeleteNodesOperation> subOperationsOut;
		return DeleteNodes(topo, intList, null, out subOperationsOut);
	}
}
