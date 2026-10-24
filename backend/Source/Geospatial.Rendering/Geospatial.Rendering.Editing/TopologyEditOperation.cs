using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Rendering.Editing;

public abstract class TopologyEditOperation : NodeArcEditOperation
{
	public static readonly bool AutoPopulateMeshValues;

	protected Stack<SavedArc> _deletedArcs = new Stack<SavedArc>();

	protected Stack<SavedNode> _deletedNodes = new Stack<SavedNode>();

	protected Stack<SavedControlPoint> _deletedControlPoints = new Stack<SavedControlPoint>();

	protected TopologyEditContext _context;

	public IReadOnlyCollection<SavedNode> DeletedNodes => _deletedNodes;

	public IReadOnlyCollection<SavedArc> DeletedArcs => _deletedArcs;

	public TopologyEditOperation(TopologyEditContext context, [AllowNull] HitTestArgs args)
		: base(context, args)
	{
		_context = context;
	}

	protected void SetRegionSelection(int fid)
	{
		_context.ArcSelectedSet.Clear();
		_context.NodeSelectedSet.Clear();
		if (_context.ControlPointSelectedSet.Count != 1 || _context.ControlPointSelectedSet.First() != fid)
		{
			_context.ControlPointSelectedSet.Clear();
			_context.ControlPointSelectedSet.Add(fid);
		}
	}
}
