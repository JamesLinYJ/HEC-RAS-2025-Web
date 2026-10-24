namespace Geospatial.Rendering.Editing;

public abstract class GraphEditOperation : NodeArcEditOperation
{
	protected GraphEditContext _context;

	public GraphEditOperation(GraphEditContext context, HitTestArgs args)
		: base(context, args)
	{
		_context = context;
	}
}
