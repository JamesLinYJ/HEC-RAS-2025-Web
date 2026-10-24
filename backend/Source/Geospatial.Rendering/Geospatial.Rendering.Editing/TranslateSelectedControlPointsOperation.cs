using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class TranslateSelectedControlPointsOperation : TopologyEditOperation
{
	private Vector _delta;

	public override string Description => "Move Control Points";

	public TranslateSelectedControlPointsOperation(TopologyEditContext context, HitTestArgs args, Vector delta)
		: base(context, args)
	{
		_delta = delta;
	}

	protected override bool ApplyInternal()
	{
		TranslateSelection(_delta);
		return true;
	}

	protected override bool UndoInternal()
	{
		UndoSelectionState();
		TranslateSelection(_delta.Negate());
		return true;
	}

	private void TranslateSelection(Vector delta)
	{
		foreach (int element in _context.ControlPointSelectedSet.Elements)
		{
			Point point = _context.Topology.RegionControlPoints[element];
			_context.Topology.UpdateControlPointPoint(element, point.Translate(delta));
		}
	}
}
