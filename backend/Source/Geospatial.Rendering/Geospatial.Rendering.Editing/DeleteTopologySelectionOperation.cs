using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Rendering.Editing;

public class DeleteTopologySelectionOperation : TopologyEditOperation
{
	private IList<int> _selectedCtrlPts;

	private IList<int> _selectedArcs;

	private IList<int> _selectedNodes;

	private DeleteControlPointsOperation _deleteCtrlPtsOp;

	private DeleteArcsOperation _deleteArcsOp;

	private MultiDeleteNodesOperation _deleteNodesOp;

	public override string Description => "Delete Topology Selected Features";

	public DeleteTopologySelectionOperation(TopologyEditContext context, [AllowNull] HitTestArgs args)
		: base(context, args)
	{
		_selectedCtrlPts = context.ControlPointSelectedSet.Elements.ToList();
		_selectedArcs = context.ArcSelectedSet.Elements.ToList();
		_selectedNodes = context.NodeSelectedSet.Elements.ToList();
	}

	protected override bool ApplyInternal()
	{
		ClearAllSelection();
		IList<int> intList;
		if (_selectedCtrlPts.Count != 0)
		{
			_deleteCtrlPtsOp = new DeleteControlPointsOperation(_context, _selectedCtrlPts);
			_deleteCtrlPtsOp.Apply();
			List<int> intListList = new List<int>();
			foreach (int selectedArc in _selectedArcs)
			{
				bool condition = false;
				int ordinal = 0;
				foreach (SavedArc deletedArc in _deleteCtrlPtsOp.DeletedArcs)
				{
					if (deletedArc.FID == selectedArc)
					{
						condition = true;
						break;
					}
					if (deletedArc.FID < selectedArc)
					{
						ordinal++;
					}
				}
				if (!condition)
				{
					intListList.Add(selectedArc - ordinal);
				}
			}
			intList = intListList;
		}
		else
		{
			intList = _selectedArcs;
		}
		if (intList.Count > 0)
		{
			_deleteArcsOp = new DeleteArcsOperation(_context, intList);
			_deleteArcsOp.Apply();
		}
		if (_selectedNodes.Count > 0)
		{
			List<int> selectedNodes = UpdateNodeIndices(_selectedNodes, _deleteCtrlPtsOp?.DeletedNodes);
			List<int> nodeIndices = UpdateNodeIndices(selectedNodes, _deleteArcsOp?.DeletedNodes);
			if (nodeIndices.Count > 0)
			{
				_deleteNodesOp = new MultiDeleteNodesOperation(_context, nodeIndices);
				_deleteNodesOp.Apply();
			}
		}
		return true;
	}

	private List<int> UpdateNodeIndices(IList<int> selectedNodes, IReadOnlyCollection<SavedNode> deletedNodes)
	{
		List<int> nodeIndices = new List<int>();
		foreach (int orderBy in selectedNodes.OrderBy((int node) => node))
		{
			bool condition = false;
			int ordinal = 0;
			if (deletedNodes != null)
			{
				foreach (SavedNode deletedNode in deletedNodes)
				{
					if (deletedNode.FID == orderBy)
					{
						condition = true;
						break;
					}
					if (deletedNode.FID < orderBy)
					{
						ordinal++;
					}
				}
			}
			if (!condition)
			{
				nodeIndices.Add(orderBy - ordinal);
			}
		}
		return nodeIndices;
	}

	protected override bool UndoInternal()
	{
		_deleteNodesOp?.Undo();
		_deleteArcsOp?.Undo();
		_deleteCtrlPtsOp?.Undo();
		UndoSelectionState();
		return true;
	}
}
