namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class DeleteTopologySelectionHandler : IEditHandler
{
	private readonly TopologyEditContext _context;

	public DeleteTopologySelectionHandler(TopologyEditContext context)
	{
		_context = context;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.Delete)
		{
			return false;
		}
		if (_context.ArcSelectedSet.Count <= 0 && _context.NodeSelectedSet.Count <= 0)
		{
			return _context.ControlPointSelectedSet.Count > 0;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		ctx.ExecuteEditOperation(new DeleteTopologySelectionOperation(_context, cmd.Location));
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
