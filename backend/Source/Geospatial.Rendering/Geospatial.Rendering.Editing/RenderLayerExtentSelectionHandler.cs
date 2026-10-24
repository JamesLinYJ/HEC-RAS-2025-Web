using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class RenderLayerExtentSelectionHandler : IEditHandler
{
	private readonly Func<IVectorRenderLayer> _getActiveLayer;

	private ExtentSelector _selector;

	private MarqueeHitMode _pendingHitMode;

	public RenderLayerExtentSelectionHandler(Func<IVectorRenderLayer> getActiveLayer)
	{
		_getActiveLayer = getActiveLayer ?? throw new ArgumentNullException("getActiveLayer");
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
		return cmd.Command switch
		{
			CommandType.BeginDragging => HandleBegin(cmd, ctx), 
			CommandType.Dragging => HandleDrag(cmd, ctx), 
			CommandType.FinishDragging => HandleFinish(cmd, ctx), 
			CommandType.CancelDragging => HandleCancel(ctx), 
			CommandType.Close => HandleCancel(ctx), 
			_ => HandlerResult.NotHandled, 
		};
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
		IVectorRenderLayer selectedLayer = GetSelectedLayer();
		if (selectedLayer == null || cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		_pendingHitMode = (ctx?.Options?.Get(LassoSelectionHandler.Options.HitMode)).GetValueOrDefault();
		IEditModifiers modifiers2 = cmd.Modifiers;
		if (modifiers2 == null || !modifiers2.IsCtrlDown())
		{
			selectedLayer.Select(Extent.Infinite, SelectMode.Remove);
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
			extentSelector.End(cmd.Location.QueryPoint, SelectMode.Toggle);
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
			_selector = new ExtentSelector(ctx.VectorManager, ApplySelection);
		}
		return _selector;
	}

	private void ApplySelection(Extent extent, SelectMode mode)
	{
		if (!extent.IsFiniteAndValid())
		{
			return;
		}
		IVectorRenderLayer selectedLayer = GetSelectedLayer();
		if (selectedLayer != null)
		{
			if (_pendingHitMode == MarqueeHitMode.Contained)
			{
				selectedLayer.Select(extent.ToPolygon(), MarqueeHitMode.Contained, mode);
			}
			else
			{
				selectedLayer.Select(extent, mode);
			}
		}
	}

	private IVectorRenderLayer GetSelectedLayer()
	{
		return _getActiveLayer();
	}
}
