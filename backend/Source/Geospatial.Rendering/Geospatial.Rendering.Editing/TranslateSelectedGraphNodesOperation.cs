using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Graph;

namespace Geospatial.Rendering.Editing;

public class TranslateSelectedGraphNodesOperation : GraphEditOperation
{
	private readonly Vector _delta;

	private readonly int[] _nodeFids;

	private readonly Dictionary<int, Point> _originalPositions = new Dictionary<int, Point>();

	public override string Description => "Move Nodes";

	public TranslateSelectedGraphNodesOperation(GraphEditContext context, HitTestArgs args, Vector delta)
		: base(context, args)
	{
		_delta = delta;
		_nodeFids = context.NodeSelectedSet.Elements.ToArray();
	}

	protected override bool ApplyInternal()
	{
		Graph graph = _context.Graph;
		_originalPositions.Clear();
		int[] nodeFids = _nodeFids;
		foreach (int nodeFidIndex in nodeFids)
		{
			Point point = graph.Nodes[nodeFidIndex].Point;
			_originalPositions[nodeFidIndex] = point;
			graph.MoveNode(nodeFidIndex, point.Translate(_delta));
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		Graph graph = _context.Graph;
		foreach (KeyValuePair<int, Point> originalPosition in _originalPositions)
		{
			graph.MoveNode(originalPosition.Key, originalPosition.Value);
		}
		UndoSelectionState();
		return true;
	}
}
