using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class SnapPolylineByLassoHandler : IEditHandler
{
	private readonly IPolylineCollection _polylines;

	private readonly Func<IResample<float>> _getRaster;

	private LassoSelector _selector;

	private EditContext _gestureCtx;

	private double _gestureCellSize;

	public SnapPolylineByLassoHandler(IPolylineCollection polylines, Func<IResample<float>> getRaster)
	{
		_polylines = polylines;
		_getRaster = getRaster;
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
		if (_getRaster?.Invoke() == null)
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
		_gestureCtx = ctx;
		_gestureCellSize = cmd.Location?.Window.CellSize ?? 0.0;
		lassoSelector.End(SelectMode.Replace);
		_gestureCtx = null;
		_gestureCellSize = 0.0;
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
			_selector = new LassoSelector(ctx.VectorManager, ApplySnap);
		}
		return _selector;
	}

	private void ApplySnap(Polygon lasso, SelectMode mode)
	{
		if (_gestureCtx == null)
		{
			return;
		}
		IResample<float> resample = _getRaster?.Invoke();
		if (resample == null)
		{
			return;
		}
		List<int> polylinesIn = PolylineMergeHelpers.FindPolylinesIn(lasso, _polylines);
		if (polylinesIn.Count != 0)
		{
			PolylineSnapOptions options = SnapPolylineByClickHandler.ReadOptions(_gestureCtx, _gestureCellSize);
			List<Polyline> polylines = new List<Polyline>(polylinesIn.Count);
			for (int i = 0; i < polylinesIn.Count; i++)
			{
				polylines.Add(PolylineSnapHelpers.SnapPolyline(_polylines[polylinesIn[i]], resample, options));
			}
			string description = ((polylinesIn.Count == 1) ? "Snap polyline" : $"Snap {polylinesIn.Count} polylines");
			ReplacePolylinesOperation op = new ReplacePolylinesOperation(_polylines, polylinesIn, polylines, description);
			_gestureCtx.ExecuteEditOperation(op);
		}
	}
}
