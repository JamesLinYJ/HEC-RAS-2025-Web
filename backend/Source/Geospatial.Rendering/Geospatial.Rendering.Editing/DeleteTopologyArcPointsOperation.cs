using System;
using System.Collections.Generic;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class DeleteTopologyArcPointsOperation : DeleteArcPointsOperation
{
	private readonly TopologyEditContext _context;

	private readonly Stack<SavedArc> _deletedArcs = new Stack<SavedArc>();

	private readonly Stack<SavedNode> _deletedNodes = new Stack<SavedNode>();

	private readonly Stack<SavedControlPoint> _deletedControlPoints = new Stack<SavedControlPoint>();

	public DeleteTopologyArcPointsOperation(TopologyEditContext context, int arcFid, Func<IPointCollection> getPointCollection, SelectedSet selectedPoints)
		: base(context, arcFid, getPointCollection, selectedPoints)
	{
		_context = context;
	}

	protected override void DeleteArc()
	{
		_deletedArcs.Clear();
		_deletedNodes.Clear();
		_deletedControlPoints.Clear();
		DeleteArcsOperation.TryDeleteArc(_arcFid, _context, in _deletedArcs, in _deletedNodes, in _deletedControlPoints);
	}

	protected override void RestoreArc()
	{
		DeleteNodesOperation.UndoDeleteNodes(_deletedNodes, _context);
		DeleteArcsOperation.UndoDeleteArcs(_deletedArcs, _deletedControlPoints, _context);
	}
}
