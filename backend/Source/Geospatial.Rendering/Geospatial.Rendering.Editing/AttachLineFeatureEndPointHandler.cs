using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class AttachLineFeatureEndPointHandler : IEditHandler
{
	private readonly EditablePolyline _editable;

	public AttachLineFeatureEndPointHandler(EditablePolyline editable)
	{
		_editable = editable;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.Click)
		{
			return type == CommandType.BeginDragging;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (_editable.TryAttachEndPoint(cmd.Location, out var _))
		{
			if (cmd.Command == CommandType.Click)
			{
				return HandlerResult.WasHandled;
			}
			if (cmd.Command == CommandType.BeginDragging)
			{
				return MoveLineFeaturePointsHandler<Polyline, EditablePolyline>.CreateAndBeginDrag(cmd, ctx, _editable);
			}
			throw new EditOperationInvalidStateException("Invalid command type");
		}
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		return _editable.TryPreviewAttachEndPoint(hit);
	}
}
