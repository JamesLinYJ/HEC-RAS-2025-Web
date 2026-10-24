using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class SetNodePointOperation : TopologyEditOperation
{
	private int _nodeIndex;

	private Point _originalPoint;

	private Point _newPoint;

	public override string Description => "Set Node Point";

	public SetNodePointOperation(TopologyEditContext context, int nodeIndex, Point newPoint)
		: base(context, null)
	{
		_nodeIndex = nodeIndex;
		_newPoint = newPoint;
		_originalPoint = context.Topology.Nodes[nodeIndex].Point;
	}

	protected override bool ApplyInternal()
	{
		_context.Topology.MoveNode(_nodeIndex, _newPoint);
		return true;
	}

	protected override bool UndoInternal()
	{
		_context.Topology.MoveNode(_nodeIndex, _originalPoint);
		UndoSelectionState();
		return true;
	}
}
