namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class DeleteArcPointsHandler : IEditHandler
{
	private readonly NodeArcEditContext _context;

	private readonly EditableArc _editable;

	public DeleteArcPointsHandler(NodeArcEditContext context, EditableArc editable)
	{
		_context = context;
		_editable = editable;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Delete;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		DeleteArcPointsOperation deleteArcPointsOperation = _context.CreateDeleteArcPointsOperation(_editable.ArcFID, () => _editable.Vector, _editable.SelectedPoints);
		ctx.ExecuteEditOperation(deleteArcPointsOperation);
		if (!deleteArcPointsOperation.ArcWasDeleted)
		{
			_editable.Redraw();
			return HandlerResult.WasHandled;
		}
		return new HandlerResult
		{
			Handled = true,
			RequestParentReleaseCapture = true,
			RequestParentTrackCaptureStateChanged = true
		};
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		return false;
	}
}
