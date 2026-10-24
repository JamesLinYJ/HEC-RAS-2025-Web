using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class SetArcInternalPointOperation : TopologyEditOperation
{
	private int _arcIndex;

	private int _internalPointIndex;

	private Point _originalPoint;

	private Point _newPoint;

	public override string Description => "Set Arc Internal Point";

	public SetArcInternalPointOperation(TopologyEditContext context, int arcIndex, int internalPointIndex, Point newPoint)
		: base(context, null)
	{
		_arcIndex = arcIndex;
		_internalPointIndex = internalPointIndex;
		_newPoint = newPoint;
		_originalPoint = context.Topology.Arcs[arcIndex].InternalPoints[internalPointIndex];
	}

	protected override bool ApplyInternal()
	{
		_context.Topology.Arcs[_arcIndex].InternalPoints[_internalPointIndex] = _newPoint;
		return true;
	}

	protected override bool UndoInternal()
	{
		_context.Topology.Arcs[_arcIndex].InternalPoints[_internalPointIndex] = _originalPoint;
		UndoSelectionState();
		return true;
	}
}
