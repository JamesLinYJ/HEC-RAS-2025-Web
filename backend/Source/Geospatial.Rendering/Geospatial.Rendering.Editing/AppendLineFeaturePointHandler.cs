using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class AppendLineFeaturePointHandler<T, K> : IEditHandler where T : Polyline, new() where K : GenericEditableLine<T>
{
	private readonly K _editable;

	public AppendLineFeaturePointHandler(K editable)
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
		if (!_editable.TryAppendPoint(cmd.Location))
		{
			return HandlerResult.NotHandled;
		}
		if (cmd.Command == CommandType.Click)
		{
			return HandlerResult.WasHandled;
		}
		if (cmd.Command == CommandType.BeginDragging)
		{
			HandlerResult handlerResult = new MoveLineFeaturePointsHandler<T, K>(_editable).Handle(cmd, ctx);
			if (!handlerResult.Handled || !handlerResult.IsCaptured)
			{
				throw new EditOperationInvalidStateException("MoveLineFeaturePointsHandler should've handled and captured");
			}
			return handlerResult;
		}
		throw new EditOperationInvalidStateException("Invalid command.");
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		_editable.PreviewAppendPoint(hit.QueryPoint);
		return true;
	}
}
