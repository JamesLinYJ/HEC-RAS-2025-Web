using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class ReplaceArcInternalPointsOperation : TopologyEditOperation
{
	private readonly string _description;

	private readonly int _arcIndex;

	private readonly PointCollection _newInternalPoints;

	private PointCollection _originalInternalPoints;

	public override string Description => _description;

	public ReplaceArcInternalPointsOperation(TopologyEditContext context, int arcIndex, PointCollection newInternalPoints, string description = "Replace Arc Internal Points")
		: base(context, null)
	{
		_arcIndex = arcIndex;
		_newInternalPoints = newInternalPoints;
		_description = description;
	}

	protected override bool ApplyInternal()
	{
		_originalInternalPoints = new PointCollection(_context.Topology.Arcs[_arcIndex].InternalPoints);
		_context.Topology.Arcs[_arcIndex].InternalPoints = _newInternalPoints;
		return true;
	}

	protected override bool UndoInternal()
	{
		_context.Topology.Arcs[_arcIndex].InternalPoints = _originalInternalPoints;
		UndoSelectionState();
		return true;
	}
}
