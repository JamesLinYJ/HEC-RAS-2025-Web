using System;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.TiledRenderingSystem;
using Geospatial.Vectors;
using Utility.Systems;

namespace Geospatial.Rendering.Systems.Tools;

public static class DefaultMapTools
{
	public static MapTool CreatePanTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer)
	{
		ArgumentNullException.ThrowIfNull(manager, "manager");
		ArgumentNullException.ThrowIfNull(keyState, "keyState");
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		ToolOrchestrator toolOrchestrator = CreateOrchestrator(manager);
		toolOrchestrator.Router.Clear();
		RegisterLayerInteractionHandlers(toolOrchestrator, manager, getActiveLayer);
		toolOrchestrator.Router.Register(new PanInteractionHandler(manager, handleLeftButton: true, handleRightButton: false), 200);
		toolOrchestrator.Router.Register(new RenderLayerClickSelectionHandler(getActiveLayer, handleDoubleClick: false), 150);
		return new MapTool(manager, toolOrchestrator, isIsolatedAction: true, keyState, allowInertia: true, segregatedActions: true, MapToolCursorKind.Pan);
	}

	public static MapTool CreateBasicPanTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState)
	{
		ArgumentNullException.ThrowIfNull(manager, "manager");
		ArgumentNullException.ThrowIfNull(keyState, "keyState");
		ToolOrchestrator toolOrchestrator = CreateOrchestrator(manager);
		toolOrchestrator.Router.Clear();
		toolOrchestrator.Router.Register(new PanInteractionHandler(manager, handleLeftButton: true, handleRightButton: false), 200);
		return new MapTool(manager, toolOrchestrator, isIsolatedAction: true, keyState, allowInertia: true, segregatedActions: true, MapToolCursorKind.Pan);
	}

	public static MapTool CreateExtentSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, OptionStore options = null)
	{
		return BuildSelectionTool(manager, keyState, getActiveLayer, new RenderLayerExtentSelectionHandler(getActiveLayer), options);
	}

	public static MapTool CreateExtentDrawTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Action<Extent> onExtentDrawn)
	{
		ArgumentNullException.ThrowIfNull(manager, "manager");
		ArgumentNullException.ThrowIfNull(keyState, "keyState");
		ArgumentNullException.ThrowIfNull(onExtentDrawn, "onExtentDrawn");
		ToolOrchestrator toolOrchestrator = CreateOrchestrator(manager);
		toolOrchestrator.Router.Clear();
		toolOrchestrator.Router.Register(new ExtentDrawHandler(onExtentDrawn), 150);
		return new MapTool(manager, toolOrchestrator, isIsolatedAction: true, keyState, allowInertia: false, segregatedActions: false, MapToolCursorKind.Selection);
	}

	public static MapTool CreatePathSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer)
	{
		return BuildSelectionTool(manager, keyState, getActiveLayer, new PathSelectionHandler(CreateLayerPathSelector(getActiveLayer)));
	}

	public static MapTool CreateLassoSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, OptionStore options = null)
	{
		return BuildSelectionTool(manager, keyState, getActiveLayer, new LassoSelectionHandler(CreateLayerLassoSelector(getActiveLayer)), options);
	}

	public static MapTool CreateTopologyExtentSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, TopologyEditContext context)
	{
		ArgumentNullException.ThrowIfNull(context, "context");
		return BuildSelectionTool(manager, keyState, getActiveLayer, new ExtentSelectionHandler(context.Select), context.SelectionOptions);
	}

	public static MapTool CreateTopologyPathSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, TopologyEditContext context)
	{
		ArgumentNullException.ThrowIfNull(context, "context");
		return BuildSelectionTool(manager, keyState, getActiveLayer, new PathSelectionHandler(context.Select), context.SelectionOptions);
	}

	public static MapTool CreateTopologyLassoSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, TopologyEditContext context)
	{
		ArgumentNullException.ThrowIfNull(context, "context");
		return BuildSelectionTool(manager, keyState, getActiveLayer, new LassoSelectionHandler(context.Select), context.SelectionOptions);
	}

	public static MapTool CreateGraphExtentSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, GraphEditContext context)
	{
		ArgumentNullException.ThrowIfNull(context, "context");
		return BuildSelectionTool(manager, keyState, getActiveLayer, new ExtentSelectionHandler(context.Select), context.SelectionOptions);
	}

	public static MapTool CreateGraphPathSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, GraphEditContext context)
	{
		ArgumentNullException.ThrowIfNull(context, "context");
		return BuildSelectionTool(manager, keyState, getActiveLayer, new PathSelectionHandler(context.Select), context.SelectionOptions);
	}

	public static MapTool CreateGraphLassoSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, GraphEditContext context)
	{
		ArgumentNullException.ThrowIfNull(context, "context");
		return BuildSelectionTool(manager, keyState, getActiveLayer, new LassoSelectionHandler(context.Select), context.SelectionOptions);
	}

	private static MapTool BuildSelectionTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, Func<IVectorRenderLayer> getActiveLayer, IEditHandler selectionHandler, OptionStore options = null)
	{
		ArgumentNullException.ThrowIfNull(manager, "manager");
		ArgumentNullException.ThrowIfNull(keyState, "keyState");
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		ArgumentNullException.ThrowIfNull(selectionHandler, "selectionHandler");
		ToolOrchestrator toolOrchestrator = CreateOrchestrator(manager);
		toolOrchestrator.Router.Clear();
		if (options != null)
		{
			toolOrchestrator.Options = options;
		}
		RegisterLayerInteractionHandlers(toolOrchestrator, manager, getActiveLayer);
		toolOrchestrator.Router.Register(selectionHandler, 150);
		toolOrchestrator.Router.Register(new RenderLayerClickSelectionHandler(getActiveLayer, handleDoubleClick: false), 100);
		return new MapTool(manager, toolOrchestrator, isIsolatedAction: true, keyState, allowInertia: false, segregatedActions: false, MapToolCursorKind.Selection);
	}

	private static Action<Polyline, SelectMode> CreateLayerPathSelector(Func<IVectorRenderLayer> getLayer)
	{
		ArgumentNullException.ThrowIfNull(getLayer, "getLayer");
		return (Polyline line, SelectMode mode) =>
		{
			IVectorRenderLayer vectorRenderLayer = getLayer();
			if (vectorRenderLayer != null)
			{
				if (line == null)
				{
					vectorRenderLayer.Select(Extent.Infinite, SelectMode.Remove);
				}
				else
				{
					vectorRenderLayer.Select(line, mode);
				}
			}
		};
	}

	private static Action<Polygon, MarqueeHitMode, SelectMode> CreateLayerLassoSelector(Func<IVectorRenderLayer> getLayer)
	{
		ArgumentNullException.ThrowIfNull(getLayer, "getLayer");
		return (Polygon lasso, MarqueeHitMode hitMode, SelectMode mode) =>
		{
			IVectorRenderLayer vectorRenderLayer = getLayer();
			if (vectorRenderLayer != null)
			{
				if (lasso == null)
				{
					vectorRenderLayer.Select(Extent.Infinite, SelectMode.Remove);
				}
				else
				{
					vectorRenderLayer.Select(lasso, hitMode, mode);
				}
			}
		};
	}

	internal static void RegisterLayerInteractionHandlers(ToolOrchestrator orchestrator, PyramidedTileManager manager, Func<IVectorRenderLayer> getActiveLayer)
	{
		orchestrator.Router.Register(new LayerHoverHighlightHandler(getActiveLayer, DefaultEditSymbology.SelectLineSymbology), 1000);
		orchestrator.Router.Register(new LayerSelectionHighlightHandler(() => getActiveLayer(), () => GetLayerSelectionVersion(getActiveLayer()), DefaultEditSymbology.TopologySelectedSymbology, DefaultEditSymbology.TopologySelectedRegionSymbology), 1001);
	}

	private static long GetLayerSelectionVersion(object layer)
	{
		if (layer is IVectorRenderLayer vectorRenderLayer)
		{
			return vectorRenderLayer.GetSelectionVersion();
		}
		if (layer is RenderLayer renderLayer)
		{
			return renderLayer.GetRenderState();
		}
		return 0L;
	}

	public static MapTool CreateFocusedEditTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, EditHandlerRouter targetRouter, Func<ToolOrchestrator> orchestratorGetter, OptionStore targetOptions)
	{
		ArgumentNullException.ThrowIfNull(manager, "manager");
		ArgumentNullException.ThrowIfNull(keyState, "keyState");
		ArgumentNullException.ThrowIfNull(targetRouter, "targetRouter");
		ArgumentNullException.ThrowIfNull(orchestratorGetter, "orchestratorGetter");
		return new MapTool(manager, () =>
		{
			ToolOrchestrator toolOrchestrator = orchestratorGetter();
			if (toolOrchestrator != null && toolOrchestrator.Router != targetRouter)
			{
				toolOrchestrator.Router = targetRouter;
			}
			if (toolOrchestrator != null && toolOrchestrator.Options != targetOptions)
			{
				toolOrchestrator.Options = targetOptions;
			}
			return toolOrchestrator;
		}, isIsolatedAction: true, keyState, allowInertia: false, segregatedActions: false, MapToolCursorKind.Edit);
	}

	public static MapTool CreateMeasureLengthTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, MeasureSlopeDelegate slope)
	{
		return BuildMeasureTool(manager, keyState, MeasureMode.Length, slope);
	}

	public static MapTool CreateMeasureAreaTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState)
	{
		return BuildMeasureTool(manager, keyState, MeasureMode.Area, null);
	}

	private static MapTool BuildMeasureTool(PyramidedTileManager manager, Func<KeyCode, bool> keyState, MeasureMode mode, MeasureSlopeDelegate slope)
	{
		ArgumentNullException.ThrowIfNull(manager, "manager");
		ArgumentNullException.ThrowIfNull(keyState, "keyState");
		ToolOrchestrator toolOrchestrator = CreateOrchestrator(manager);
		toolOrchestrator.RegisterMeasureToolHandlers(mode, slope);
		toolOrchestrator.Router.Register(new PanInteractionHandler(manager, handleLeftButton: false), 25);
		return new MapTool(manager, toolOrchestrator, isIsolatedAction: true, keyState, allowInertia: false, segregatedActions: false, MapToolCursorKind.Edit);
	}

	internal static ToolOrchestrator CreateOrchestrator(PyramidedTileManager manager)
	{
		ToolOrchestrator toolOrchestrator = new ToolOrchestrator(manager.VectorManager, manager.MapSyncContext, manager.UIThreadSynchronizationContext);
		toolOrchestrator.ActivateAnnotations();
		return toolOrchestrator;
	}
}
