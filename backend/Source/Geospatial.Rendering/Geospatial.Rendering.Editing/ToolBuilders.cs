using System;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing.Handlers;
using Geospatial.Rendering.Editing.Handlers.Topology;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public static class ToolBuilders
{
	private static void RegisterMarquee(EditHandlerRouter r, Action<Extent, SelectMode> selectExt, Action<Polyline, SelectMode> selectPath, Action<Polygon, MarqueeHitMode, SelectMode> selectLasso)
	{
		r.Register(new MarqueeSelectionHandler(selectExt, selectPath, selectLasso), 50);
	}

	public static void RegisterGraphSketchHandlers(EditHandlerRouter router, IVectorManager vectorManager, GraphEditContext context)
	{
		router.Clear();
		router.Register(new DeleteGraphSelectionHandler(context), 400);
		router.Register(new SplitArcHandler(context, SplitArcHandler.Options.Mode, GraphSplitCapture(context)), 350);
		router.Register(new SelectNodeHandler(context), 310);
		router.Register(new SelectArcHandler(context), 300);
		router.Register(CreateOpenArcHandler(vectorManager, context), 200);
		router.Register(new MoveNodeHandler(context), 155);
		router.Register(new AddNewArcHandler(context), 100);
		router.Register(CreateGraphMarqueeHandler(context), 50);
	}

	public static EditHandlerRouter BuildGraphSketchRouter(IVectorManager vectorManager, GraphEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterGraphSketchHandlers(editHandlerRouter, vectorManager, context);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildGraphAddRouter(GraphEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new SplitArcHandler(context, SplitArcHandler.Options.Mode, GraphSplitCapture(context), requireSKey: false, startNewArcOnSplit: true), 350);
		editHandlerRouter.Register(new AddNewArcHandler(context), 100);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildGraphEditRouter(IVectorManager vectorManager, GraphEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new DeleteGraphSelectionHandler(context), 400);
		editHandlerRouter.Register(new SelectNodeHandler(context), 310);
		editHandlerRouter.Register(new SelectArcHandler(context), 300);
		editHandlerRouter.Register(CreateOpenArcHandler(vectorManager, context), 200);
		editHandlerRouter.Register(new MoveNodeHandler(context), 155);
		editHandlerRouter.Register(CreateGraphMarqueeHandler(context), 50);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildGraphSplitRouter(GraphEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new SplitArcHandler(context, SplitArcHandler.Options.Mode, GraphSplitCapture(context), requireSKey: false), 350);
		editHandlerRouter.Register(new SelectArcHandler(context), 300);
		return editHandlerRouter;
	}

	private static Func<int, EditContext, Func<IEditHandler>> GraphSplitCapture(GraphEditContext context)
	{
		return (int fid, EditContext ctx) => AddNewArcHandler.GetNewArcEditCapture(fid, context, ctx);
	}

	private static MarqueeSelectionHandler CreateGraphMarqueeHandler(GraphEditContext context)
	{
		return new MarqueeSelectionHandler(context.Select, context.Select, context.Select);
	}

	public static void RegisterMeasureToolHandlers(this ToolOrchestrator editor, MeasureMode mode, MeasureSlopeDelegate getSlope)
	{
		editor.Router.Clear();
		MeasureToolHandler measureToolHandler = new MeasureToolHandler(editor.VectorManager, mode, getSlope);
		editor.BecameInactive += measureToolHandler.Reset;
		editor.Router.Register(measureToolHandler, 100);
	}

	public static void RegisterSketchPointHandlers(this ToolOrchestrator editor, IPointCollection pts, SelectedSet selected)
	{
		editor.RegisterSketchPointHandlers(pts, selected, CreatePointHitTest(pts), CreatePointExtentSelector(pts, selected));
	}

	public static void RegisterSketchPointHandlers(this ToolOrchestrator editor, IPointCollection pts, SelectedSet selected, Func<HitTestArgs, int> hitClosest, Action<Extent, SelectMode> selectExt)
	{
		editor.Router.Clear();
		editor.Router.Register(new DeleteFeatureHandler<Point>(pts, selected), 400);
		editor.Router.Register(new SelectPointFeatureHandler(hitClosest, selected, (int fid) => pts[fid]), 200);
		MovePointFeatureHandler movePointFeatureHandler = new MovePointFeatureHandler(hitClosest, selected, (int fid) => pts[fid], CreateEditable);
		editor.Router.Register(movePointFeatureHandler, 150);
		editor.BecameInactive += movePointFeatureHandler.ResetEditable;
		editor.Router.Register(new AddNewPointFeatureHandler(pts, selected), 100);
		editor.Router.Register(new ExtentSelectionHandler(selectExt), 50);
		editor.Router.Register(new DeselectPointHandler(selected), 25);
		EditablePointCollection CreateEditable(EditContext ctx)
		{
			return new EditablePointCollection(pts, ctx.VectorManager, ctx.ExecuteEditOperation, selected, ctx.Preview ?? editor.GlobalOverlay);
		}
	}

	public static Func<HitTestArgs, int> CreatePointHitTest(IPointCollection points)
	{
		return (HitTestArgs args) =>
		{
			if (points == null)
			{
				return -1;
			}
			int nearestFeature;
			return HitTestHelpers.TryHitTestClosestFeature(args.QueryPoint, 8.0 * args.Window.CellSize, points.PointSpatialIndex, (Point pt, int fid) => points[fid].DistanceTo(pt), out nearestFeature) ? nearestFeature : (-1);
		};
	}

	public static Action<Extent, SelectMode> CreatePointExtentSelector(IPointCollection points, SelectedSet selected)
	{
		return (Extent ext, SelectMode mode) =>
		{
			VectorRenderLayer.SelectHelper(ext, selected, points.Count, mode, points, ext, (int fid, IPointCollection layer, Extent extent) => PointRenderLayer.HitTest(layer, fid, extent), (IPointCollection layer) => layer.PointSpatialIndex);
		};
	}

	public static Action<Polygon, MarqueeHitMode, SelectMode> CreatePointLassoSelector(IPointCollection points, SelectedSet selected)
	{
		return (Polygon lasso, MarqueeHitMode hitMode, SelectMode mode) =>
		{
			if (lasso == null)
			{
				selected.Clear();
			}
			else
			{
				VectorRenderLayer.SelectHelper(lasso.Extent, selected, points.Count, mode, points, lasso, (int fid, IPointCollection layer, Polygon polygon) => polygon.Contains(layer[fid]), (IPointCollection layer) => layer.PointSpatialIndex);
			}
		};
	}

	public static void RegisterSketchPolygonHandlers(this ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected, bool allowManualAdd = true)
	{
		editor.RegisterSketchPolygonHandlers(polygons, selected, CreatePolygonHitTest(polygons), CreatePolygonExtentSelector(polygons, selected), allowManualAdd);
	}

	public static void RegisterSketchPolygonHandlers(this ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected, Func<HitTestArgs, int> hitClosest, Action<Extent, SelectMode> selectExt, bool allowManualAdd = true)
	{
		editor.Router.Clear();
		RegisterPolygonSketchInto(editor.Router, editor, polygons, selected, hitClosest, selectExt, allowManualAdd, CreatePolygonPathSelector(polygons, selected), CreatePolygonLassoSelector(polygons, selected));
	}

	public static void RegisterStampPolygonHandlers(this ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected, Func<HitTestArgs, int> hitClosest, Action<Extent, SelectMode> selectExt, Func<Polygon> templateFactory)
	{
		editor.RegisterSketchPolygonHandlers(polygons, selected, hitClosest, selectExt, allowManualAdd: false);
		editor.Router.Register(new StampPolygonPlacementHandler(polygons, selected, templateFactory, hitClosest), 175);
	}

	public static void RegisterStampPolygonHandlers(this ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected, Func<Polygon> templateFactory)
	{
		editor.RegisterStampPolygonHandlers(polygons, selected, CreatePolygonHitTest(polygons), CreatePolygonExtentSelector(polygons, selected), templateFactory);
	}

	public static Func<HitTestArgs, int> CreatePolygonHitTest(IPolygonCollection polygons)
	{
		return (HitTestArgs args) =>
		{
			if (polygons == null)
			{
				return -1;
			}
			(int, double) result;
			return polygons.TryHitTestByBestArea(args.QueryPoint, polygons.GetSpatialIndex(), 8.0 * args.Window.CellSize, out result) ? result.Item1 : (-1);
		};
	}

	public static Action<Extent, SelectMode> CreatePolygonExtentSelector(IPolygonCollection polygons, SelectedSet selected)
	{
		return (Extent ext, SelectMode mode) =>
		{
			VectorRenderLayer.SelectHelper(ext, selected, polygons.Count, mode, polygons, ext, (int fid, IPolygonCollection layer, Extent extent) => layer.HitTest(fid, extent, 0.0), (IPolygonCollection layer) => layer.GetSpatialIndex());
		};
	}

	public static Action<Polyline, SelectMode> CreatePolygonPathSelector(IPolygonCollection polygons, SelectedSet selected)
	{
		return (Polyline line, SelectMode mode) =>
		{
			if (line == null)
			{
				selected.Clear();
			}
			else
			{
				VectorRenderLayer.SelectHelper(line.Extent, selected, polygons.Count, mode, polygons, line, (int fid, IPolygonCollection layer, Polyline intersectLine) => layer.HitTest(fid, intersectLine, 0.0), (IPolygonCollection layer) => layer.GetSpatialIndex());
			}
		};
	}

	public static Action<Polygon, MarqueeHitMode, SelectMode> CreatePolygonLassoSelector(IPolygonCollection polygons, SelectedSet selected)
	{
		return (Polygon lasso, MarqueeHitMode hitMode, SelectMode mode) =>
		{
			if (lasso == null)
			{
				selected.Clear();
			}
			else
			{
				VectorRenderLayer.SelectHelper(lasso.Extent, selected, polygons.Count, mode, polygons, (lasso, hitMode), (int fid, IPolygonCollection layer, (Polygon lasso, MarqueeHitMode hitMode) payload) => (payload.hitMode != MarqueeHitMode.Contained) ? payload.lasso.AreaIntersects(layer[fid]) : payload.lasso.Contains(layer[fid]), (IPolygonCollection layer) => layer.GetSpatialIndex());
			}
		};
	}

	public static EditHandlerRouter BuildPolygonSketchRouter(ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterPolygonSketchInto(editHandlerRouter, editor, polygons, selected, CreatePolygonHitTest(polygons), CreatePolygonExtentSelector(polygons, selected), allowManualAdd: true, CreatePolygonPathSelector(polygons, selected), CreatePolygonLassoSelector(polygons, selected));
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolygonAddRouter(ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new AddNewLineFeatureHandler<Polygon, EditablePolygon>(polygons, selected, (int fid, IVectorManager vm, EditContext ctx) => new EditablePolygon(polygons[fid], vm, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay)), 150);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolygonEditRouter(ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected)
	{
		Func<HitTestArgs, int> func = CreatePolygonHitTest(polygons);
		Action<Extent, SelectMode> selectExt = CreatePolygonExtentSelector(polygons, selected);
		Action<Polyline, SelectMode> selectPath = CreatePolygonPathSelector(polygons, selected);
		Action<Polygon, MarqueeHitMode, SelectMode> selectLasso = CreatePolygonLassoSelector(polygons, selected);
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new DeleteFeatureHandler<Polygon>(polygons, selected), 400);
		editHandlerRouter.Register(CreatePolygonOpenHandler(editor, polygons, func), 200);
		editHandlerRouter.Register(new SelectPolygonFeatureHandler(func, selected, (int fid) => polygons[fid]), 150);
		RegisterMarquee(editHandlerRouter, selectExt, selectPath, selectLasso);
		return editHandlerRouter;
	}

	private static void RegisterPolygonSketchInto(EditHandlerRouter r, ToolOrchestrator editor, IPolygonCollection polygons, SelectedSet selected, Func<HitTestArgs, int> hitClosest, Action<Extent, SelectMode> selectExt, bool allowManualAdd, Action<Polyline, SelectMode> selectPath, Action<Polygon, MarqueeHitMode, SelectMode> selectLasso)
	{
		r.Register(new DeleteFeatureHandler<Polygon>(polygons, selected), 400);
		r.Register(CreatePolygonOpenHandler(editor, polygons, hitClosest), 200);
		if (allowManualAdd)
		{
			r.Register(new AddNewLineFeatureHandler<Polygon, EditablePolygon>(polygons, selected, (int fid, IVectorManager vm, EditContext ctx) => new EditablePolygon(polygons[fid], vm, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay)), 150);
		}
		r.Register(new SelectPolygonFeatureHandler(hitClosest, selected, (int fid) => polygons[fid]), 150);
		RegisterMarquee(r, selectExt, selectPath, selectLasso);
	}

	private static OpenPolygonFeatureHandler CreatePolygonOpenHandler(ToolOrchestrator editor, IPolygonCollection polygons, Func<HitTestArgs, int> hitClosest)
	{
		return new OpenPolygonFeatureHandler(hitClosest, (int fid, EditContext ctx) =>
		{
			EditablePolygon editable = new EditablePolygon(polygons[fid], editor.VectorManager, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay);
			return new EditLineFeatureHandler<Polygon, EditablePolygon>(editable, fid, (EditHandlerRouter parent) =>
			{
				parent.Register(new PointsDeleteHandler<Polygon, EditablePolygon>(editable, () =>
				{
					new DeleteFeatureHandler<Polygon>(polygons, new SelectedSet { fid }, silently: true).Handle(new EditCommand(CommandType.Delete, null), ctx);
				}), 50);
				parent.Register(new MoveLineFeaturePointsHandler<Polygon, EditablePolygon>(editable), 45);
				parent.Register(new InsertLineFeaturePointHandler<Polygon, EditablePolygon>(editable), 40);
				parent.Register(new SelectLineFeaturePointHandler<Polygon, EditablePolygon>(editable), 15);
				parent.Register(new ExtentSelectionHandler((Extent e2, SelectMode m) =>
				{
					editable.TrySelectPoints(e2, m);
				}), 10);
			});
		}, (int fid) => polygons[fid]);
	}

	public static void RegisterSketchPolylineHandlers(this ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected)
	{
		editor.RegisterSketchPolylineHandlers(polylines, selected, CreatePolylineHitTest(polylines), CreatePolylineExtentSelector(polylines, selected));
	}

	public static void RegisterSketchPolylineHandlers(this ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected, Func<HitTestArgs, int> hitClosest, Action<Extent, SelectMode> selectExt)
	{
		editor.Router.Clear();
		RegisterPolylineSketchInto(editor.Router, editor, polylines, selected, hitClosest, selectExt, splitRequiresSKey: true, CreatePolylinePathSelector(polylines, selected), CreatePolylineLassoSelector(polylines, selected));
	}

	public static Func<HitTestArgs, int> CreatePolylineHitTest(IPolylineCollection polylines)
	{
		return (HitTestArgs args) =>
		{
			if (polylines == null)
			{
				return -1;
			}
			int plIdx;
			return polylines.TryHitTestClosestFeature(args.QueryPoint, 8.0 * args.Window.CellSize, out plIdx) ? plIdx : (-1);
		};
	}

	public static Action<Extent, SelectMode> CreatePolylineExtentSelector(IPolylineCollection polylines, SelectedSet selected)
	{
		return (Extent ext, SelectMode mode) =>
		{
			VectorRenderLayer.SelectHelper(ext, selected, polylines.Count, mode, polylines, ext, (int fid, IPolylineCollection layer, Extent extent) => PolylineRenderLayer.HitTestExtent(layer, extent, fid), (IPolylineCollection layer) => layer.GetSpatialIndex());
		};
	}

	public static Action<Polyline, SelectMode> CreatePolylinePathSelector(IPolylineCollection polylines, SelectedSet selected)
	{
		return (Polyline line, SelectMode mode) =>
		{
			if (line == null)
			{
				selected.Clear();
			}
			else
			{
				VectorRenderLayer.SelectHelper(line.Extent, selected, polylines.Count, mode, polylines, line, (int fid, IPolylineCollection layer, Polyline intersectLine) => intersectLine.Intersects(layer[fid]), (IPolylineCollection layer) => layer.GetSpatialIndex());
			}
		};
	}

	public static Action<Polygon, MarqueeHitMode, SelectMode> CreatePolylineLassoSelector(IPolylineCollection polylines, SelectedSet selected)
	{
		return (Polygon lasso, MarqueeHitMode hitMode, SelectMode mode) =>
		{
			if (lasso == null)
			{
				selected.Clear();
			}
			else
			{
				VectorRenderLayer.SelectHelper(lasso.Extent, selected, polylines.Count, mode, polylines, (lasso, hitMode), (int fid, IPolylineCollection layer, (Polygon lasso, MarqueeHitMode hitMode) payload) =>
				{
					Polyline polyline = layer[fid];
					if (payload.hitMode == MarqueeHitMode.Contained)
					{
						return payload.lasso.Contains(polyline);
					}
					return payload.lasso.ContainsAny(polyline.Points) || payload.lasso.Intersects(polyline);
				}, (IPolylineCollection layer) => layer.GetSpatialIndex());
			}
		};
	}

	public static EditHandlerRouter BuildPolylineSketchRouter(ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterPolylineSketchInto(editHandlerRouter, editor, polylines, selected, CreatePolylineHitTest(polylines), CreatePolylineExtentSelector(polylines, selected), splitRequiresSKey: true, CreatePolylinePathSelector(polylines, selected), CreatePolylineLassoSelector(polylines, selected));
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineAddRouter(ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new AddNewLineFeatureHandler<Polyline, EditablePolyline>(createFeature: (int fid, IVectorManager vm, EditContext ctx) => new EditablePolyline(polylines[fid], vm, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay), features: polylines, selected: selected), 150);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineSmartAddRouter(ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected, Func<RasterDefinition, Memory<float>, bool> batchSample)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		if (batchSample == null)
		{
			return editHandlerRouter;
		}
		Action<EditHandlerRouter, EditablePolyline> registerCapturedChildHandlers = (EditHandlerRouter childRouter, EditablePolyline editable) =>
		{
			childRouter.Register(new SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>(editable, batchSample), 40);
			childRouter.Register(new AppendLineFeaturePointHandler<Polyline, EditablePolyline>(editable), 30);
		};
		Func<int, IVectorManager, EditContext, EditablePolyline> createFeature = (int fid, IVectorManager vm, EditContext ctx) => new EditablePolyline(polylines[fid], vm, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay);
		editHandlerRouter.Register(new AddNewLineFeatureHandler<Polyline, EditablePolyline>(polylines, selected, createFeature, registerCapturedChildHandlers), 150);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineDeepConnectRouter(ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected, SnapSelector.CandidateSupplier supplier)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		if (supplier == null)
		{
			return editHandlerRouter;
		}
		SnapSelector snap = new SnapSelector(editor.VectorManager, supplier);
		Action<EditHandlerRouter, EditablePolyline> registerCapturedChildHandlers = (EditHandlerRouter childRouter, EditablePolyline editable) =>
		{
			childRouter.Register(new DeepConnectTraceAppendHandler<Polyline, EditablePolyline>(editable, snap), 40);
			childRouter.Register(new AppendLineFeaturePointHandler<Polyline, EditablePolyline>(editable), 30);
		};
		Func<int, IVectorManager, EditContext, EditablePolyline> createFeature = (int fid, IVectorManager vm, EditContext ctx) => new EditablePolyline(polylines[fid], vm, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay);
		editHandlerRouter.Register(new SnapDecorator(new AddNewLineFeatureHandler<Polyline, EditablePolyline>(polylines, selected, createFeature, registerCapturedChildHandlers), snap), 150);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineEditRouter(ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected)
	{
		Func<HitTestArgs, int> func = CreatePolylineHitTest(polylines);
		Action<Extent, SelectMode> selectExt = CreatePolylineExtentSelector(polylines, selected);
		Action<Polyline, SelectMode> selectPath = CreatePolylinePathSelector(polylines, selected);
		Action<Polygon, MarqueeHitMode, SelectMode> selectLasso = CreatePolylineLassoSelector(polylines, selected);
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new DeleteFeatureHandler<Polyline>(polylines, selected), 400);
		editHandlerRouter.Register(CreatePolylineOpenHandler(editor, polylines, func), 200);
		editHandlerRouter.Register(new SelectLineFeatureHandler(func, selected, (int fid) => polylines[fid]), 150);
		RegisterMarquee(editHandlerRouter, selectExt, selectPath, selectLasso);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineSplitRouter(ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected)
	{
		Func<HitTestArgs, int> hitTest = CreatePolylineHitTest(polylines);
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new SplitPolylineHandler(polylines, requireSKey: false), 350);
		editHandlerRouter.Register(new SelectLineFeatureHandler(hitTest, selected, (int fid) => polylines[fid]), 100);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineGpReverseRouter(IPolylineCollection polylines)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new ReversePolylineHandler(polylines), 300);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineGpSplitRouter(IPolylineCollection polylines)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new SplitPolylineHandler(polylines, requireSKey: false), 350);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineGpMergeRouter(IPolylineCollection polylines, SelectedSet selected)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new MergePolylineByClickHandler(polylines, selected), 320);
		editHandlerRouter.Register(new MergePolylineByLassoHandler(polylines, selected), 50);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineGpSnapRouter(IPolylineCollection polylines, Func<IResample<float>> getRaster)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new SnapPolylineByClickHandler(polylines, getRaster), 320);
		editHandlerRouter.Register(new SnapPolylineByLassoHandler(polylines, getRaster), 50);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineGpOffsetRouter(IPolylineCollection polylines)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new OffsetPolylineByClickHandler(polylines), 300);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineGpFilterRouter(IPolylineCollection polylines)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new FilterPolylineHandler(polylines), 300);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildPolylineGpSmoothRouter(IPolylineCollection polylines)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		editHandlerRouter.Register(new SmoothPolylineHandler(polylines), 300);
		return editHandlerRouter;
	}

	private static void RegisterPolylineSketchInto(EditHandlerRouter r, ToolOrchestrator editor, IPolylineCollection polylines, SelectedSet selected, Func<HitTestArgs, int> hitClosest, Action<Extent, SelectMode> selectExt, bool splitRequiresSKey, Action<Polyline, SelectMode> selectPath, Action<Polygon, MarqueeHitMode, SelectMode> selectLasso)
	{
		r.Register(new DeleteFeatureHandler<Polyline>(polylines, selected), 400);
		r.Register(new SplitPolylineHandler(polylines, splitRequiresSKey), 340);
		r.Register(new MergePolylineByLassoHandler(polylines, selected, requireMKey: true), 330);
		r.Register(CreatePolylineOpenHandler(editor, polylines, hitClosest), 200);
		r.Register(new AddNewLineFeatureHandler<Polyline, EditablePolyline>(polylines, selected, (int fid, IVectorManager vm, EditContext ctx) => new EditablePolyline(polylines[fid], vm, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay)), 150);
		r.Register(new SelectLineFeatureHandler(hitClosest, selected, (int fid) => polylines[fid]), 150);
		RegisterMarquee(r, selectExt, selectPath, selectLasso);
	}

	private static OpenLineFeatureHandler CreatePolylineOpenHandler(ToolOrchestrator editor, IPolylineCollection polylines, Func<HitTestArgs, int> hitClosest)
	{
		return new OpenLineFeatureHandler(hitClosest, (int fid, EditContext ctx) =>
		{
			EditablePolyline editable = new EditablePolyline(polylines[fid], editor.VectorManager, ctx.ExecuteEditOperation, new SelectedSet(), editor.GlobalOverlay);
			return new EditLineFeatureHandler<Polyline, EditablePolyline>(editable, fid, (EditHandlerRouter parent) =>
			{
				parent.Register(new PointsDeleteHandler<Polyline, EditablePolyline>(editable, () =>
				{
					new DeleteFeatureHandler<Polyline>(polylines, new SelectedSet { fid }, silently: true).Handle(new EditCommand(CommandType.Delete, null), ctx);
				}), 50);
				parent.Register(new MoveLineFeaturePointsHandler<Polyline, EditablePolyline>(editable), 45);
				parent.Register(new InsertLineFeaturePointHandler<Polyline, EditablePolyline>(editable), 40);
				parent.Register(new SelectLineFeaturePointHandler<Polyline, EditablePolyline>(editable), 35);
				parent.Register(new AttachLineFeatureEndPointHandler(editable), 25);
				parent.Register(new ExtentSelectionHandler((Extent e2, SelectMode m) =>
				{
					editable.TrySelectPoints(e2, m);
				}), 10);
			});
		}, (int fid) => polylines[fid]);
	}

	public static void RegisterTopologySketchHandlers(EditHandlerRouter router, IVectorManager vectorManager, TopologyEditContext context)
	{
		router.Clear();
		RegisterTopologyErrorPreview(router, context);
		router.Register(new DeleteTopologySelectionHandler(context), 400);
		router.Register(new SplitArcHandler(context, SplitArcHandler.Options.Mode, TopologySplitCapture(context)), 350);
		router.Register(new SelectNodeHandler(context), 310);
		router.Register(new SelectArcHandler(context), 300);
		router.Register(new SelectRegionHandler(context), 290);
		router.Register(CreateOpenArcHandler(vectorManager, context), 200);
		router.Register(new MoveNodeHandler(context), 155);
		router.Register(new AddNewArcHandler(context), 100);
		router.Register(CreateTopologyMarqueeHandler(context), 50);
	}

	public static EditHandlerRouter BuildTopologySketchRouter(IVectorManager vectorManager, TopologyEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologySketchHandlers(editHandlerRouter, vectorManager, context);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologyAddRouter(TopologyEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		editHandlerRouter.Register(new SplitArcHandler(context, SplitArcHandler.Options.Mode, TopologySplitCapture(context), requireSKey: false, startNewArcOnSplit: true), 350);
		editHandlerRouter.Register(new AddNewArcHandler(context), 100);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologyEditRouter(IVectorManager vectorManager, TopologyEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		editHandlerRouter.Register(new DeleteTopologySelectionHandler(context), 400);
		editHandlerRouter.Register(new SelectNodeHandler(context), 310);
		editHandlerRouter.Register(new SelectArcHandler(context), 300);
		editHandlerRouter.Register(new SelectRegionHandler(context), 290);
		editHandlerRouter.Register(CreateOpenArcHandler(vectorManager, context), 200);
		editHandlerRouter.Register(new MoveNodeHandler(context), 155);
		editHandlerRouter.Register(CreateTopologyMarqueeHandler(context), 50);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologySplitRouter(TopologyEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		editHandlerRouter.Register(new SplitArcHandler(context, SplitArcHandler.Options.Mode, TopologySplitCapture(context), requireSKey: false), 350);
		editHandlerRouter.Register(new SelectArcHandler(context), 300);
		return editHandlerRouter;
	}

	internal static OpenArcHandler CreateOpenArcHandler(IVectorManager vectorManager, NodeArcEditContext context)
	{
		return new OpenArcHandler(context, (HitTestArgs hit) => (NetworkRenderLayer.HitTest(context.Network, hit, out var element, out var elementIndex, out var _) && (element == NetworkElementType.ArcSegment || element == NetworkElementType.ArcInternalPoint)) ? elementIndex : (-1), (int arcId, EditContext ctx) =>
		{
			EditableArc editable = new EditableArc(arcId, new Any<BaseTopology>(() => context.Network), EditableArc.EditingMode.Editing, vectorManager, ctx.ExecuteEditOperation, context.OpenFeatureSelectedSetCache, context.EditableNetwork.GlobalOverlay);
			return new EditLineFeatureHandler<ArcPolylineWrapper, EditableArc>(editable, arcId, (EditHandlerRouter child) =>
			{
				child.Register(new DeleteArcPointsHandler(context, editable), 50);
				child.Register(new MoveArcPointsHandler(context, editable), 45);
				child.Register(new InsertLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>(editable), 40);
				child.Register(new SelectLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>(editable), 15);
				child.Register(new ExtentSelectionHandler((Extent ext, SelectMode mode) =>
				{
					editable.TrySelectPoints(ext, mode);
				}), 10);
			});
		});
	}

	private static MarqueeSelectionHandler CreateTopologyMarqueeHandler(TopologyEditContext context)
	{
		return new MarqueeSelectionHandler(context.Select, context.Select, context.Select);
	}

	private static void RegisterTopologyErrorPreview(EditHandlerRouter router, TopologyEditContext context)
	{
		router.Register(new ErrorPreviewHandler(context), 999);
	}

	private static Func<int, EditContext, Func<IEditHandler>> TopologySplitCapture(TopologyEditContext context, Action<EditHandlerRouter, EditableArc> registerChildren = null)
	{
		return (int fid, EditContext ctx) => AddNewArcHandler.GetNewArcEditCapture(fid, context, ctx, registerChildren);
	}

	public static EditHandlerRouter BuildTopologyGpReverseRouter(TopologyEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		editHandlerRouter.Register(new ReverseArcHandler(context), 300);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologyGpSnapRouter(TopologyEditContext context, Func<IResample<float>> getRaster)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		editHandlerRouter.Register(new SnapArcByClickHandler(context, getRaster), 320);
		editHandlerRouter.Register(new SnapArcByLassoHandler(context, getRaster), 50);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologyGpFilterRouter(TopologyEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		editHandlerRouter.Register(new FilterArcHandler(context), 300);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologyGpSmoothRouter(TopologyEditContext context)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		editHandlerRouter.Register(new SmoothArcHandler(context), 300);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologySmartAddRouter(TopologyEditContext context, Func<RasterDefinition, Memory<float>, bool> batchSample)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		if (batchSample == null)
		{
			return editHandlerRouter;
		}
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		Action<EditHandlerRouter, EditableArc> action = (EditHandlerRouter childRouter, EditableArc editable) =>
		{
			childRouter.Register(new SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>(editable, batchSample), 40);
			childRouter.Register(new AppendArcPointHandler(editable, context), 30);
		};
		editHandlerRouter.Register(new SplitArcHandler(context, SplitArcHandler.Options.Mode, TopologySplitCapture(context, action), requireSKey: false, startNewArcOnSplit: true), 350);
		editHandlerRouter.Register(new AddNewArcHandler(context, -1, action), 100);
		return editHandlerRouter;
	}

	public static EditHandlerRouter BuildTopologyDeepConnectRouter(TopologyEditContext context, ToolOrchestrator editor, SnapSelector.CandidateSupplier supplier)
	{
		EditHandlerRouter editHandlerRouter = new EditHandlerRouter();
		if (supplier == null)
		{
			return editHandlerRouter;
		}
		RegisterTopologyErrorPreview(editHandlerRouter, context);
		SnapSelector snap = new SnapSelector(editor.VectorManager, supplier);
		Action<EditHandlerRouter, EditableArc> action = (EditHandlerRouter childRouter, EditableArc editable) =>
		{
			childRouter.Register(new DeepConnectTraceAppendHandler<ArcPolylineWrapper, EditableArc>(editable, snap), 40);
			childRouter.Register(new AppendArcPointHandler(editable, context), 30);
		};
		editHandlerRouter.Register(new SnapDecorator(new SplitArcHandler(context, SplitArcHandler.Options.Mode, TopologySplitCapture(context, action), requireSKey: false, startNewArcOnSplit: true), snap), 350);
		editHandlerRouter.Register(new SnapDecorator(new AddNewArcHandler(context, -1, action), snap), 100);
		return editHandlerRouter;
	}
}
