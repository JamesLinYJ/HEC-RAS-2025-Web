namespace Geospatial.Rendering.Editing;

public sealed class DeleteGraphSelectionHandler : IEditHandler
{
	private readonly GraphEditContext _context;

	public DeleteGraphSelectionHandler(GraphEditContext context)
	{
		_context = context;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.Delete)
		{
			return false;
		}
		if (_context.ArcSelectedSet.Count <= 0)
		{
			return _context.NodeSelectedSet.Count > 0;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		ctx.ExecuteEditOperation(new DeleteGraphSelectionOperation(_context, cmd.Location));
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
