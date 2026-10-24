using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Graph;
using Geospatial.Vectors.Topology;
using Utility.Memory;

namespace Geospatial.Rendering.Editing;

public class DeleteGraphSelectionOperation : GraphEditOperation
{
	private struct SavedArc
	{
		public int FID;

		public GraphArc Arc;

		public object[] Metadata;
	}

	private struct SavedNode
	{
		public int FID;

		public Point Point;

		public object[] Metadata;
	}

	private readonly List<int> _selectedArcs;

	private readonly List<int> _selectedNodes;

	private List<SavedArc> _deletedArcs;

	private List<SavedNode> _deletedNodes;

	public override string Description => "Delete Selected Features";

	public DeleteGraphSelectionOperation(GraphEditContext context, HitTestArgs args)
		: base(context, args)
	{
		_selectedArcs = context.ArcSelectedSet.Elements.ToList();
		_selectedNodes = context.NodeSelectedSet.Elements.ToList();
	}

	protected override bool ApplyInternal()
	{
		ClearAllSelection();
		Graph graph = _context.Graph;
		HashSet<int> selectedArcs = new HashSet<int>(_selectedArcs);
		foreach (int selectedNode in _selectedNodes)
		{
			foreach (NodeArcConnection arc in graph.Nodes[selectedNode].Arcs)
			{
				selectedArcs.Add(arc.ArcID);
			}
		}
		using (_context.BeginBatchUpdate())
		{
			_deletedArcs = new List<SavedArc>(selectedArcs.Count);
			foreach (int orderByDescending in selectedArcs.OrderByDescending((int x) => x))
			{
				_deletedArcs.Add(new SavedArc
				{
					FID = orderByDescending,
					Arc = graph.Arcs[orderByDescending].Clone(),
					Metadata = (object[])graph.ArcMetadata.Rows[orderByDescending].ItemArray.Clone()
				});
				graph.RemoveArc(orderByDescending);
				_context.OnArcRemoved(orderByDescending);
			}
			_deletedNodes = new List<SavedNode>(_selectedNodes.Count);
			foreach (int orderByDescending2 in _selectedNodes.OrderByDescending((int x) => x))
			{
				_deletedNodes.Add(new SavedNode
				{
					FID = orderByDescending2,
					Point = graph.Nodes[orderByDescending2].Point,
					Metadata = (object[])graph.NodeMetadata.Rows[orderByDescending2].ItemArray.Clone()
				});
				graph.RemoveNode(orderByDescending2);
				_context.OnNodeRemoved(orderByDescending2);
			}
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		Graph graph = _context.Graph;
		using (_context.BeginBatchUpdate())
		{
			foreach (SavedNode orderBy in _deletedNodes.OrderBy((SavedNode s) => s.FID))
			{
				TableRow tableRow = graph.InsertNode(orderBy.FID, orderBy.Point);
				orderBy.Metadata.CopyTo(tableRow.ItemArray, 0);
				_context.OnNodeInserted(orderBy.FID);
			}
			foreach (SavedArc orderBy2 in _deletedArcs.OrderBy((SavedArc s) => s.FID))
			{
				TableRow tableRow2 = graph.InsertArc(orderBy2.FID, orderBy2.Arc.Clone());
				orderBy2.Metadata.CopyTo(tableRow2.ItemArray, 0);
				_context.OnArcInserted(orderBy2.FID);
			}
		}
		UndoSelectionState();
		return true;
	}
}
