using System.Collections.Generic;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class SelectLineFeaturePointHandler<T, K> : IEditHandler where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection where K : GenericEditableLine<T>
{
	private readonly K _editable;

	public SelectLineFeaturePointHandler(K editable)
	{
		_editable = editable;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.Click;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (_editable.TrySelectPoints(cmd.Location, (!cmd.Modifiers.IsCtrlDown()) ? SelectMode.Replace : SelectMode.Toggle))
		{
			return HandlerResult.WasHandled;
		}
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		return _editable.TryPreviewToggleSelectedPoints(hit);
	}
}
