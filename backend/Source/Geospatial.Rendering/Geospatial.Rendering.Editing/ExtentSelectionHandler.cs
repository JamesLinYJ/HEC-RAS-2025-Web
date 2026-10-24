using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class ExtentSelectionHandler : IEditHandler
{
	private ExtentSelector _selector;

	private Action<Extent, SelectMode> _select;

	public ExtentSelectionHandler(Action<Extent, SelectMode> select)
	{
		_select = select;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return type == CommandType.BeginDragging;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (cmd.Command == CommandType.BeginDragging)
		{
			if (_selector == null)
			{
				_selector = new ExtentSelector(ctx.VectorManager, _select);
			}
			_selector.Begin(cmd.Location.QueryPoint);
			return new HandlerResult
			{
				Handled = true,
				GetCapture = () => this
			};
		}
		if (cmd.Command == CommandType.FinishDragging)
		{
			SelectMode mode = ((!cmd.Modifiers.IsCtrlDown()) ? SelectMode.Replace : SelectMode.Toggle);
			_selector.End(cmd.Location.QueryPoint, mode);
			return new HandlerResult
			{
				Handled = true,
				ReleaseCapture = true
			};
		}
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		ExtentSelector selector = _selector;
		if (selector == null || !selector.IsActive)
		{
			return false;
		}
		if (hit == null)
		{
			return false;
		}
		_selector.Render(hit.QueryPoint);
		return true;
	}
}
