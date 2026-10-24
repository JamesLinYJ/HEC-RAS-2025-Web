using System.Collections.Generic;
using System.Drawing;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class MergePolylineByLassoHandler : IEditHandler
{
	public static class Options
	{
		public static readonly OptionKey<double> ToleranceWorld = OptionKey<double>.Define("Polyline.Merge.ToleranceWorld", "Merge Tolerance (world units)", "Endpoint match tolerance, in map units.", 5.0);
	}

	private const string HintMarkerChannel = "merge_preview_hint";

	private readonly IPolylineCollection _polylines;

	private readonly SelectedSet _selectedSet;

	private readonly bool _requireMKey;

	private LassoSelector _selector;

	private EditContext _gestureCtx;

	private VectorID _hintMarkerId;

	public MergePolylineByLassoHandler(IPolylineCollection polylines, SelectedSet selectedSet, bool requireMKey = false)
	{
		_polylines = polylines;
		_selectedSet = selectedSet;
		_requireMKey = requireMKey;
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
		if (lassoSelector.IsActive)
		{
			lassoSelector.Render();
			return true;
		}
		lassoSelector.Clear();
		if (hit == null)
		{
			return false;
		}
		if (!_requireMKey)
		{
			return false;
		}
		if (modifiers == null || !modifiers.IsAdditionalKeyDown(PolylineMergeHelpers.MergeKey))
		{
			return false;
		}
		ctx.Preview.TryGetVector("merge_preview_hint", out _hintMarkerId);
		Extent extent = new Extent(hit.QueryPoint, hit.QueryPoint).Extend(hit.Window.CellSize * 5.0);
		ctx.VectorManager.AddOrUpdateVector(ref _hintMarkerId, Polygon.FromExtent(extent), new VectorSymbology(Color.FromArgb(255, 255, 0, 0), 1.0, Color.FromArgb(64, 255, 0, 0)));
		ctx.Preview.RegisterVector("merge_preview_hint", _hintMarkerId);
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
		if (_requireMKey)
		{
			IEditModifiers modifiers2 = cmd.Modifiers;
			if (modifiers2 == null || !modifiers2.IsAdditionalKeyDown(PolylineMergeHelpers.MergeKey))
			{
				return HandlerResult.NotHandled;
			}
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
		lassoSelector.End(SelectMode.Replace);
		_gestureCtx = null;
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
			_selector = new LassoSelector(ctx.VectorManager, ApplyMerge);
		}
		return _selector;
	}

	private void ApplyMerge(Polygon lasso, SelectMode mode)
	{
		if (_gestureCtx != null)
		{
			List<int> polylinesIn = PolylineMergeHelpers.FindPolylinesIn(lasso, _polylines);
			if (polylinesIn.Count >= 2)
			{
				double tolerance = _gestureCtx.Options.Get(Options.ToleranceWorld);
				MergePolylineOperation op = new MergePolylineOperation(_polylines, polylinesIn, tolerance);
				_gestureCtx.ExecuteEditOperation(op);
				_selectedSet?.Clear();
			}
		}
	}
}
