using System;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class LassoSelectionHandler : IEditHandler
{
	public static class Options
	{
		public static readonly OptionKey<MarqueeHitMode> HitMode = OptionKey<MarqueeHitMode>.Define("Lasso.HitMode", "Hit Mode", "Whether selection takes any feature it touches, or only features fully inside.", MarqueeHitMode.Intersects);
	}

	private readonly Action<Polygon, MarqueeHitMode, SelectMode> _select;

	private LassoSelector _selector;

	private SelectMode _pendingMode;

	private MarqueeHitMode _pendingHitMode;

	public LassoSelectionHandler(Action<Polygon, MarqueeHitMode, SelectMode> select)
	{
		_select = select ?? throw new ArgumentNullException("select");
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
		LassoSelector lassoSelector = EnsureSelector(ctx);
		if (!lassoSelector.IsActive)
		{
			lassoSelector.Clear();
			return false;
		}
		lassoSelector.Render();
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
		MarqueeHitMode marqueeHitMode = (_pendingHitMode = ReadHitMode(ctx));
		IEditModifiers modifiers2 = cmd.Modifiers;
		_pendingMode = ((modifiers2 == null || !modifiers2.IsCtrlDown()) ? SelectMode.Replace : SelectMode.Toggle);
		IEditModifiers modifiers3 = cmd.Modifiers;
		if (modifiers3 == null || !modifiers3.IsCtrlDown())
		{
			_select(null, marqueeHitMode, SelectMode.Remove);
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
		LassoSelector lassoSelector = EnsureSelector(ctx);
		if (!lassoSelector.IsActive || cmd.Location == null)
		{
			return HandlerResult.NotHandled;
		}
		lassoSelector.AddPoint(cmd.Location.QueryPoint);
		lassoSelector.Render();
		return HandlerResult.WasHandled;
	}

	private HandlerResult HandleFinish(EditCommand cmd, EditContext ctx)
	{
		LassoSelector lassoSelector = EnsureSelector(ctx);
		if (!lassoSelector.IsActive)
		{
			return HandlerResult.NotHandled;
		}
		lassoSelector.End(_pendingMode);
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private HandlerResult HandleCancel(EditContext ctx)
	{
		LassoSelector lassoSelector = EnsureSelector(ctx);
		if (!lassoSelector.IsActive)
		{
			return HandlerResult.NotHandled;
		}
		lassoSelector.Quit();
		return new HandlerResult
		{
			Handled = true,
			ReleaseCapture = true
		};
	}

	private LassoSelector EnsureSelector(EditContext ctx)
	{
		if (_selector == null)
		{
			_selector = new LassoSelector(ctx.VectorManager, ApplySelection);
		}
		return _selector;
	}

	private void ApplySelection(Polygon lasso, SelectMode mode)
	{
		_select(lasso, _pendingHitMode, mode);
	}

	private static MarqueeHitMode ReadHitMode(EditContext ctx)
	{
		return (ctx?.Options?.Get(Options.HitMode)).GetValueOrDefault();
	}
}
