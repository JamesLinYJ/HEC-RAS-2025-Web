using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class ExtentDrawHandler : IEditHandler
{
	private readonly Action<Extent> _onExtentDrawn;

	private ExtentSelector _selector;

	public ExtentDrawHandler(Action<Extent> onExtentDrawn)
	{
		_onExtentDrawn = onExtentDrawn ?? throw new ArgumentNullException("onExtentDrawn");
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (type != CommandType.BeginDragging && type != CommandType.Dragging && type != CommandType.FinishDragging)
		{
			return type == CommandType.CancelDragging;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		switch (cmd.Command)
		{
		case CommandType.BeginDragging:
			return HandleBegin(cmd, ctx);
		case CommandType.Dragging:
			return HandleDrag(cmd, ctx);
		case CommandType.FinishDragging:
			return HandleFinish(cmd, ctx);
		case CommandType.Close:
		case CommandType.CancelDragging:
			return HandleCancel(ctx);
		default:
			return HandlerResult.NotHandled;
		}
	}

	public bool Preview(HitTestArgs hit, IEditModifiers modifiers, EditContext ctx)
	{
		ExtentSelector extentSelector = EnsureSelector(ctx);
		if (!extentSelector.IsActive || hit == null)
		{
			return false;
		}
		extentSelector.Render(hit.QueryPoint);
		return true;
	}

	private HandlerResult HandleBegin(EditCommand cmd, EditContext ctx)
	{
		IEditModifiers modifiers = cmd.Modifiers;
		if (modifiers != null && modifiers.IsRightClick())
		{
			return HandlerResult.NotHandled;
		}
		if (cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		EnsureSelector(ctx).Begin(cmd.Location.QueryPoint);
		return new HandlerResult
		{
			Handled = true,
			GetCapture = () => this
		};
	}

	private HandlerResult HandleDrag(EditCommand cmd, EditContext ctx)
	{
		ExtentSelector extentSelector = EnsureSelector(ctx);
		if (!extentSelector.IsActive || cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		extentSelector.Render(cmd.Location.QueryPoint);
		return HandlerResult.WasHandled;
	}

	private HandlerResult HandleFinish(EditCommand cmd, EditContext ctx)
	{
		ExtentSelector extentSelector = EnsureSelector(ctx);
		if (!extentSelector.IsActive)
		{
			return HandlerResult.NotHandled;
		}
		if (cmd.Location != null)
		{
			extentSelector.End(cmd.Location.QueryPoint, SelectMode.Replace);
		}
		else
		{
			extentSelector.Clear();
		}
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private HandlerResult HandleCancel(EditContext ctx)
	{
		ExtentSelector extentSelector = EnsureSelector(ctx);
		if (!extentSelector.IsActive)
		{
			return HandlerResult.NotHandled;
		}
		extentSelector.Clear();
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private ExtentSelector EnsureSelector(EditContext ctx)
	{
		if (_selector == null)
		{
			_selector = new ExtentSelector(ctx.VectorManager, ApplyExtent);
		}
		return _selector;
	}

	private void ApplyExtent(Extent extent, SelectMode mode)
	{
		if (extent.IsFiniteAndValid())
		{
			_onExtentDrawn(extent);
		}
	}
}
