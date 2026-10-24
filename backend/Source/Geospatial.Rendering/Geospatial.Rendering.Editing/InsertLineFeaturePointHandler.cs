using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class InsertLineFeaturePointHandler<T, K> : IEditHandler where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection where K : GenericEditableLine<T>
{
	private readonly K _editable;

	public InsertLineFeaturePointHandler(K editable)
	{
		_editable = editable;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.BeginDragging)
		{
			return type == CommandType.Click;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (!_editable.TryInsertPoint(cmd.Location, out var _))
		{
			return HandlerResult.NotHandled;
		}
		if (cmd.Command == CommandType.BeginDragging)
		{
			return MoveLineFeaturePointsHandler<T, GenericEditableLine<T>>.CreateAndBeginDrag(cmd, ctx, _editable);
		}
		return new HandlerResult
		{
			Handled = true
		};
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		return _editable.TryPreviewInsertPoint(hit);
	}
}
