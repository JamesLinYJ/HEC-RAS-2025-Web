using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class SnapArcByLassoHandler : IEditHandler
{
	private readonly TopologyEditContext _context;

	private readonly Func<IResample<float>> _getRaster;

	private LassoSelector _selector;

	private EditContext _gestureCtx;

	private double _gestureCellSize;

	public SnapArcByLassoHandler(TopologyEditContext context, Func<IResample<float>> getRaster)
	{
		_context = context;
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
		if (_gestureCtx == null || lasso == null)
		{
			return;
		}
		IResample<float> resample = _getRaster?.Invoke();
		if (resample == null)
		{
			return;
		}
		Geospatial.Vectors.Topology.Topology topology = _context.Topology;
		List<int> arcsIn = FindArcsIn(lasso, topology);
		if (arcsIn.Count == 0)
		{
			return;
		}
		PolylineSnapOptions options = SnapPolylineByClickHandler.ReadOptions(_gestureCtx, _gestureCellSize);
		List<EditOperation> editOperations = new List<EditOperation>();
		for (int i = 0; i < arcsIn.Count; i++)
		{
			int arcsIn2 = arcsIn[i];
			if (TrySnapArcInternals(arcsIn2, topology, resample, options, out var newInternals))
			{
				ReplaceArcInternalPointsOperation replaceArcInternalPointsOperation = new ReplaceArcInternalPointsOperation(_context, arcsIn2, newInternals, "Snap arc");
				editOperations.Add(replaceArcInternalPointsOperation);
			}
		}
		_gestureCtx.ExecuteEditOperation(new MultiOperationEditOperation(editOperations));
	}

	private static bool TrySnapArcInternals(int arcFid, Geospatial.Vectors.Topology.Topology topo, IResample<float> raster, PolylineSnapOptions options, out PointCollection newInternals)
	{
		Arc arc = topo.Arcs[arcFid];
		if (arc == null || arc.InternalPointCount == 0)
		{
			newInternals = null;
			return false;
		}
		Polyline polyline = PolylineSnapHelpers.SnapPolyline(topo.GetArcPolyline(arcFid), raster, options);
		PointCollection pointCollection = new PointCollection(polyline.Count - 2);
		for (int i = 1; i < polyline.Count - 1; i++)
		{
			pointCollection.Add(polyline[i]);
		}
		newInternals = pointCollection;
		return true;
	}

	private static List<int> FindArcsIn(Polygon lasso, Geospatial.Vectors.Topology.Topology topo)
	{
		List<int> arcsIn = new List<int>();
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			Arc arc = topo.Arcs[i];
			if (arc == null || arc.InternalPointCount == 0)
			{
				continue;
			}
			bool condition = false;
			for (int j = 0; j < arc.InternalPointCount; j++)
			{
				if (lasso.Contains(arc.InternalPoints[j]))
				{
					condition = true;
					break;
				}
			}
			if (condition)
			{
				arcsIn.Add(i);
			}
		}
		return arcsIn;
	}
}
