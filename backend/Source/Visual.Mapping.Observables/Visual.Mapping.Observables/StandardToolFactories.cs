using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Editing.Handlers;
using Geospatial.Rendering.Editing.Handlers.Topology;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems.Tools;
using Geospatial.Rendering.TiledRenderingSystem;
using Geospatial.Vectors;
using Utility.Systems;

namespace Visual.Mapping.Observables;

public static class StandardToolFactories
{
	public readonly struct PolylineFocusedEditPieces(EditHandlerRouter sketchRouter, EditHandlerRouter addRouter, EditHandlerRouter editRouter, EditHandlerRouter splitRouter, ToolGroup editGroup)
	{
		public readonly EditHandlerRouter SketchRouter = sketchRouter;

		public readonly EditHandlerRouter AddRouter = addRouter;

		public readonly EditHandlerRouter EditRouter = editRouter;

		public readonly EditHandlerRouter SplitRouter = splitRouter;

		public readonly ToolGroup EditGroup = editGroup;
	}

	public static ToolGroup Selection(Func<IVectorRenderLayer> getActiveLayer, int zIndex = 100)
	{
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		OptionStore options = new OptionStore(LassoSelectionHandler.Options.HitMode);
		ToolDescriptor toolDescriptor = DefaultMapToolDescriptors.CreateSelectExtent((PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreateExtentSelectionTool(manager, keyState, getActiveLayer, options), options);
		ToolDescriptor tool = DefaultMapToolDescriptors.CreateSelectPath(getActiveLayer);
		ToolDescriptor tool2 = DefaultMapToolDescriptors.CreateSelectLasso((PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreateLassoSelectionTool(manager, keyState, getActiveLayer, options), options);
		return new ToolGroup("Select", toolDescriptor, new ToolLeaf(toolDescriptor), new ToolLeaf(tool), new ToolLeaf(tool2))
		{
			ZIndex = zIndex
		};
	}

	public static ToolGroup TopologySelection(Func<IVectorRenderLayer> getActiveLayer, TopologyEditContext context, OptionStore options, int zIndex = 100)
	{
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		ArgumentNullException.ThrowIfNull(context, "context");
		ArgumentNullException.ThrowIfNull(options, "options");
		ToolDescriptor toolDescriptor = DefaultMapToolDescriptors.CreateSelectExtent((PyramidedTileManager m, Func<KeyCode, bool> k) => DefaultMapTools.CreateTopologyExtentSelectionTool(m, k, getActiveLayer, context), options.View(TopologySelectionOptions.Scope, LassoSelectionHandler.Options.HitMode));
		ToolDescriptor tool = DefaultMapToolDescriptors.CreateSelectPath((PyramidedTileManager m, Func<KeyCode, bool> k) => DefaultMapTools.CreateTopologyPathSelectionTool(m, k, getActiveLayer, context), options.View(TopologySelectionOptions.Scope));
		ToolDescriptor tool2 = DefaultMapToolDescriptors.CreateSelectLasso((PyramidedTileManager m, Func<KeyCode, bool> k) => DefaultMapTools.CreateTopologyLassoSelectionTool(m, k, getActiveLayer, context), options.View(TopologySelectionOptions.Scope, LassoSelectionHandler.Options.HitMode));
		return new ToolGroup("Select", toolDescriptor, new ToolLeaf(toolDescriptor), new ToolLeaf(tool), new ToolLeaf(tool2))
		{
			ZIndex = zIndex
		};
	}

	public static ToolGroup GraphSelection(Func<IVectorRenderLayer> getActiveLayer, GraphEditContext context, OptionStore options, int zIndex = 100)
	{
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		ArgumentNullException.ThrowIfNull(context, "context");
		ArgumentNullException.ThrowIfNull(options, "options");
		ToolDescriptor toolDescriptor = DefaultMapToolDescriptors.CreateSelectExtent((PyramidedTileManager m, Func<KeyCode, bool> k) => DefaultMapTools.CreateGraphExtentSelectionTool(m, k, getActiveLayer, context), options.View(GraphSelectionOptions.Scope, LassoSelectionHandler.Options.HitMode));
		ToolDescriptor tool = DefaultMapToolDescriptors.CreateSelectPath((PyramidedTileManager m, Func<KeyCode, bool> k) => DefaultMapTools.CreateGraphPathSelectionTool(m, k, getActiveLayer, context), options.View(GraphSelectionOptions.Scope));
		ToolDescriptor tool2 = DefaultMapToolDescriptors.CreateSelectLasso((PyramidedTileManager m, Func<KeyCode, bool> k) => DefaultMapTools.CreateGraphLassoSelectionTool(m, k, getActiveLayer, context), options.View(GraphSelectionOptions.Scope, LassoSelectionHandler.Options.HitMode));
		return new ToolGroup("Select", toolDescriptor, new ToolLeaf(toolDescriptor), new ToolLeaf(tool), new ToolLeaf(tool2))
		{
			ZIndex = zIndex
		};
	}

	public static ToolDescriptor FocusedEditDescriptor(string id, string displayName, string iconKey, string description, EditHandlerRouter router, Func<ToolOrchestrator> orchestrator, OptionStore options = null, Func<bool> isAvailable = null)
	{
		ArgumentNullException.ThrowIfNull(router, "router");
		ArgumentNullException.ThrowIfNull(orchestrator, "orchestrator");
		return new ToolDescriptor(id, displayName, iconKey, (PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreateFocusedEditTool(manager, keyState, router, orchestrator, options))
		{
			Description = description,
			Options = options,
			IsAvailable = isAvailable
		};
	}

	public static ToolGroup MakeGroup(string name, int zIndex, ToolDescriptor defaultFace, params ToolDescriptor[] additional)
	{
		return MakeGroup(name, zIndex, isExperimental: false, defaultFace, additional);
	}

	public static ToolGroup MakeGroup(string name, int zIndex, bool isExperimental, ToolDescriptor defaultFace, params ToolDescriptor[] additional)
	{
		ArgumentException.ThrowIfNullOrEmpty(name, "name");
		ArgumentNullException.ThrowIfNull(defaultFace, "defaultFace");
		ArgumentNullException.ThrowIfNull(additional, "additional");
		ToolNode[] toolNodes = new ToolNode[1 + additional.Length];
		toolNodes[0] = new ToolLeaf(defaultFace);
		for (int i = 0; i < additional.Length; i++)
		{
			ArgumentNullException.ThrowIfNull(additional[i], "additional[i]");
			toolNodes[i + 1] = new ToolLeaf(additional[i]);
		}
		return new ToolGroup(name, defaultFace, toolNodes)
		{
			ZIndex = zIndex,
			IsExperimental = isExperimental
		};
	}

	public static ToolGroup EditGroup(ToolDescriptor sketch, params ToolDescriptor[] additional)
	{
		return MakeGroup("Edit", 200, sketch, additional);
	}

	public static IReadOnlyList<ToolNode> SingleFocusedEditLeaf(VisualContext vc, string id, string displayName, string iconKey, string description)
	{
		ArgumentNullException.ThrowIfNull(vc, "vc");
		if (!(vc.EditLayer is ToolOrchestrator toolOrchestrator))
		{
			return Array.Empty<ToolNode>();
		}
		return new _003C_003Ez__ReadOnlySingleElementList<ToolNode>(new ToolLeaf(FocusedEditDescriptor(id, displayName, iconKey, description, toolOrchestrator.Router, () => vc.EditLayer as ToolOrchestrator))
		{
			ZIndex = 200
		});
	}

	public static ToolGroup GeoprocessingGroup(ToolDescriptor face, params ToolDescriptor[] additional)
	{
		return MakeGroup("Geoprocessing", 300, isExperimental: true, face, additional);
	}

	public static IReadOnlyList<ToolNode> PolylineFocusedEditTools(VisualContext vc, IPolylineCollection polylines, SelectedSet selected, string idPrefix = "Polyline")
	{
		PolylineFocusedEditPieces pieces;
		return PolylineFocusedEditTools(vc, polylines, selected, out pieces, idPrefix);
	}

	public static IReadOnlyList<ToolNode> PolylineFocusedEditTools(VisualContext vc, IPolylineCollection polylines, SelectedSet selected, out PolylineFocusedEditPieces pieces, string idPrefix = "Polyline")
	{
		ArgumentNullException.ThrowIfNull(vc, "vc");
		ArgumentNullException.ThrowIfNull(polylines, "polylines");
		ArgumentNullException.ThrowIfNull(selected, "selected");
		if (!(vc.EditLayer is ToolOrchestrator toolOrchestrator))
		{
			pieces = default;
			return Array.Empty<ToolNode>();
		}
		EditHandlerRouter editHandlerRouter = ToolBuilders.BuildPolylineSketchRouter(toolOrchestrator, polylines, selected);
		EditHandlerRouter editHandlerRouter2 = ToolBuilders.BuildPolylineAddRouter(toolOrchestrator, polylines, selected);
		EditHandlerRouter editHandlerRouter3 = ToolBuilders.BuildPolylineEditRouter(toolOrchestrator, polylines, selected);
		EditHandlerRouter editHandlerRouter4 = ToolBuilders.BuildPolylineSplitRouter(toolOrchestrator, polylines, selected);
		toolOrchestrator.Router = editHandlerRouter;
		Func<ToolOrchestrator> orchestrator = () => vc.EditLayer as ToolOrchestrator;
		ToolDescriptor sketch = FocusedEditDescriptor(idPrefix + ".Sketch", "Edit", "Pencil", "All-in-one polyline editing: add, move, split, and merge.", editHandlerRouter, orchestrator, new OptionStore(MarqueeSelectionHandler.Options.SelectionShape, LassoSelectionHandler.Options.HitMode, MergePolylineByLassoHandler.Options.ToleranceWorld, SplitPolylineHandler.Options.Mode));
		ToolDescriptor toolDescriptor = FocusedEditDescriptor(idPrefix + ".Add", "Add", "Plus", "Click to start drawing a new polyline. Double-click an existing polyline to split and continue from there.", editHandlerRouter2, orchestrator, new OptionStore(SplitPolylineHandler.Options.Mode));
		ToolDescriptor toolDescriptor2 = FocusedEditDescriptor(idPrefix + ".Edit", "Move", "Pencil", "Move existing polyline vertices and delete features.", editHandlerRouter3, orchestrator, new OptionStore(MarqueeSelectionHandler.Options.SelectionShape, LassoSelectionHandler.Options.HitMode));
		ToolDescriptor toolDescriptor3 = FocusedEditDescriptor(idPrefix + ".Split", "Split", "Split", "Click a polyline to split it; Alt+click swaps to the non-default split mode.", editHandlerRouter4, orchestrator, new OptionStore(SplitPolylineHandler.Options.Mode));
		LayerSelectionHighlightHandler handler = new LayerSelectionHighlightHandler(() => vc.NotLazyLoadedRenderLayer as IBasicSelection, () => (vc.NotLazyLoadedRenderLayer as VectorRenderLayer)?.SelectedSet?.VersionXY ?? (-1), DefaultEditSymbology.TopologySelectedSymbology, DefaultEditSymbology.TopologySelectedRegionSymbology);
		editHandlerRouter.Register(handler, 1001);
		editHandlerRouter3.Register(handler, 1001);
		ToolGroup toolGroup = EditGroup(sketch, toolDescriptor, toolDescriptor2, toolDescriptor3);
		pieces = new PolylineFocusedEditPieces(editHandlerRouter, editHandlerRouter2, editHandlerRouter3, editHandlerRouter4, toolGroup);
		return new _003C_003Ez__ReadOnlySingleElementList<ToolNode>(toolGroup);
	}

	public static IReadOnlyList<ToolNode> PolygonFocusedEditTools(VisualContext vc, IPolygonCollection polygons, SelectedSet selected, string idPrefix = "Polygon")
	{
		ArgumentNullException.ThrowIfNull(vc, "vc");
		ArgumentNullException.ThrowIfNull(polygons, "polygons");
		ArgumentNullException.ThrowIfNull(selected, "selected");
		if (!(vc.EditLayer is ToolOrchestrator toolOrchestrator))
		{
			return Array.Empty<ToolNode>();
		}
		EditHandlerRouter editHandlerRouter = ToolBuilders.BuildPolygonSketchRouter(toolOrchestrator, polygons, selected);
		EditHandlerRouter router = ToolBuilders.BuildPolygonAddRouter(toolOrchestrator, polygons, selected);
		EditHandlerRouter editHandlerRouter2 = ToolBuilders.BuildPolygonEditRouter(toolOrchestrator, polygons, selected);
		toolOrchestrator.Router = editHandlerRouter;
		Func<ToolOrchestrator> orchestrator = () => vc.EditLayer as ToolOrchestrator;
		ToolDescriptor sketch = FocusedEditDescriptor(idPrefix + ".Sketch", "Edit", "Pencil", "All-in-one polygon editing: add features, move/insert/delete vertices.", editHandlerRouter, orchestrator, new OptionStore(MarqueeSelectionHandler.Options.SelectionShape, LassoSelectionHandler.Options.HitMode));
		ToolDescriptor toolDescriptor = FocusedEditDescriptor(idPrefix + ".Add", "Add", "Plus", "Click to start drawing a new polygon.", router, orchestrator);
		ToolDescriptor toolDescriptor2 = FocusedEditDescriptor(idPrefix + ".Edit", "Move", "Pencil", "Move existing polygon vertices and delete features.", editHandlerRouter2, orchestrator, new OptionStore(MarqueeSelectionHandler.Options.SelectionShape, LassoSelectionHandler.Options.HitMode));
		LayerSelectionHighlightHandler handler = new LayerSelectionHighlightHandler(() => vc.NotLazyLoadedRenderLayer as IBasicSelection, () => (vc.NotLazyLoadedRenderLayer as VectorRenderLayer)?.SelectedSet?.VersionXY ?? (-1), DefaultEditSymbology.TopologySelectedSymbology, DefaultEditSymbology.TopologySelectedRegionSymbology);
		editHandlerRouter.Register(handler, 1001);
		editHandlerRouter2.Register(handler, 1001);
		return new _003C_003Ez__ReadOnlySingleElementList<ToolNode>(EditGroup(sketch, toolDescriptor, toolDescriptor2));
	}

	public static IReadOnlyList<ToolNode> DefaultVectorTools(VisualContext vc, string idPrefix, Func<IReadOnlyList<ToolNode>> buildSelectionTools, Func<IReadOnlyList<ToolNode>> buildAdditionalTools = null, Func<IReadOnlyList<ToolNode>> buildEditTools = null)
	{
		ArgumentNullException.ThrowIfNull(vc, "vc");
		ArgumentNullException.ThrowIfNull(buildSelectionTools, "buildSelectionTools");
		if (!(vc is IVectorFeatureContext { IsFeatureDataLoaded: not false, SelectedFeatureIDs: var selectedFeatureIDs }))
		{
			return Array.Empty<ToolNode>();
		}
		if (selectedFeatureIDs == null)
		{
			return Array.Empty<ToolNode>();
		}
		IReadOnlyList<ToolNode> readOnlyList = buildEditTools?.Invoke();
		if (readOnlyList == null)
		{
			if (vc is IPolylineVectorContext { Polylines: not null } polylineVectorContext)
			{
				readOnlyList = PolylineFocusedEditTools(vc, polylineVectorContext.Polylines, selectedFeatureIDs, idPrefix);
			}
			else
			{
				if (!(vc is IPolygonVectorContext { Polygons: not null } polygonVectorContext))
				{
					return Array.Empty<ToolNode>();
				}
				readOnlyList = PolygonFocusedEditTools(vc, polygonVectorContext.Polygons, selectedFeatureIDs, idPrefix);
			}
		}
		if (readOnlyList.Count == 0)
		{
			return Array.Empty<ToolNode>();
		}
		IReadOnlyList<ToolNode> readOnlyList2 = buildSelectionTools();
		IReadOnlyList<ToolNode> readOnlyList3 = readOnlyList;
		IReadOnlyList<ToolNode> readOnlyList4 = buildAdditionalTools?.Invoke() ?? Array.Empty<ToolNode>();
		int ordinal = 0;
		ToolNode[] toolNodes = new ToolNode[readOnlyList2.Count + readOnlyList3.Count + readOnlyList4.Count];
		foreach (ToolNode readOnlyList22 in readOnlyList2)
		{
			toolNodes[ordinal] = readOnlyList22;
			ordinal++;
		}
		foreach (ToolNode readOnlyList32 in readOnlyList3)
		{
			toolNodes[ordinal] = readOnlyList32;
			ordinal++;
		}
		foreach (ToolNode readOnlyList42 in readOnlyList4)
		{
			toolNodes[ordinal] = readOnlyList42;
			ordinal++;
		}
		return new _003C_003Ez__ReadOnlyArray<ToolNode>(toolNodes);
	}

	public static IReadOnlyList<ToolNode> PolylineGeoprocessingTools(VisualContext vc, IPolylineCollection polylines, SelectedSet selected, string idPrefix = "Polyline", Func<IResample<float>> terrainSource = null, Func<RasterDefinition, Memory<float>, bool> batchSample = null, SnapSelector.CandidateSupplier deepConnectSupplier = null)
	{
		ArgumentNullException.ThrowIfNull(vc, "vc");
		ArgumentNullException.ThrowIfNull(polylines, "polylines");
		ArgumentNullException.ThrowIfNull(selected, "selected");
		if (!(vc.EditLayer is ToolOrchestrator editor))
		{
			return Array.Empty<ToolNode>();
		}
		EditHandlerRouter router = ToolBuilders.BuildPolylineGpReverseRouter(polylines);
		EditHandlerRouter router2 = ToolBuilders.BuildPolylineGpSplitRouter(polylines);
		EditHandlerRouter router3 = ToolBuilders.BuildPolylineGpMergeRouter(polylines, selected);
		EditHandlerRouter router4 = ToolBuilders.BuildPolylineGpSnapRouter(polylines, terrainSource);
		EditHandlerRouter router5 = ToolBuilders.BuildPolylineGpOffsetRouter(polylines);
		EditHandlerRouter router6 = ToolBuilders.BuildPolylineGpFilterRouter(polylines);
		EditHandlerRouter router7 = ToolBuilders.BuildPolylineGpSmoothRouter(polylines);
		EditHandlerRouter router8 = ToolBuilders.BuildPolylineSmartAddRouter(editor, polylines, selected, batchSample);
		EditHandlerRouter router9 = ToolBuilders.BuildPolylineDeepConnectRouter(editor, polylines, selected, deepConnectSupplier);
		Func<ToolOrchestrator> orchestrator = () => vc.EditLayer as ToolOrchestrator;
		ToolDescriptor face = FocusedEditDescriptor(idPrefix + ".GP.Reverse", "Reverse", "Refresh", "Click a polyline to reverse its direction.", router, orchestrator);
		ToolDescriptor toolDescriptor = FocusedEditDescriptor(idPrefix + ".GP.Split", "Split", "Split", "Click a polyline to split it; Alt+click swaps to the non-default split mode.", router2, orchestrator, new OptionStore(SplitPolylineHandler.Options.Mode));
		ToolDescriptor toolDescriptor2 = FocusedEditDescriptor(idPrefix + ".GP.Merge", "Merge", "Merge", "Click two polylines in sequence to merge them, or drag a closed lasso to merge contained polylines.", router3, orchestrator, new OptionStore(MergePolylineByLassoHandler.Options.ToleranceWorld));
		ToolDescriptor toolDescriptor3 = FocusedEditDescriptor(idPrefix + ".GP.Snap", "Snap", "Pin", "Click a polyline (or drag a closed lasso) to snap vertices to nearby high or low ground.", router4, orchestrator, new OptionStore(SnapTolerance.SearchRadiusPixels, SnapTolerance.SearchRadiusWorld, SnapPolylineByClickHandler.Options.SampleSpacing, SnapPolylineByClickHandler.Options.SearchMode, SnapPolylineByClickHandler.Options.ElevationMode, SnapPolylineByClickHandler.Options.PointsPerRing, SnapPolylineByClickHandler.Options.Iterations), () => terrainSource?.Invoke() != null);
		ToolDescriptor toolDescriptor4 = FocusedEditDescriptor(idPrefix + ".GP.Offset", "Offset", "Offset", "Click a polyline to replace it with its offset polylines.", router5, orchestrator, new OptionStore(OffsetPolylineByClickHandler.Options.OffsetDistance, OffsetPolylineByClickHandler.Options.Sides, OffsetPolylineByClickHandler.Options.IncludeEndpoints, OffsetPolylineByClickHandler.Options.KeepOriginal));
		ToolDescriptor toolDescriptor5 = FocusedEditDescriptor(idPrefix + ".GP.Filter", "Filter", "Filter", "Click a polyline to Douglas-Peucker simplify it. Hover to preview.", router6, orchestrator, new OptionStore(FilterPolylineHandler.Options.Epsilon));
		ToolDescriptor toolDescriptor6 = FocusedEditDescriptor(idPrefix + ".GP.Smooth", "Smooth", "Smooth", "Click a polyline to smooth it (moving average). Hover to preview.", router7, orchestrator, new OptionStore(SmoothPolylineHandler.Options.WindowSize, SmoothPolylineHandler.Options.Iterations));
		ToolDescriptor toolDescriptor7 = FocusedEditDescriptor(idPrefix + ".GP.SmartAdd", "Ridge", "AI", "Click to start drawing a polyline whose segments follow ridges/valleys between clicks (requires terrain).", router8, orchestrator, new OptionStore(SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.Algorithm, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.DpTolerancePixels, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.MaxSegmentPixels, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.PreviewThrottlePixels, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HighGroundAlpha, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HighGroundBailRatio, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HighGroundSnapRadiusCells, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HessianFrangiBeta, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HessianFeaturePenaltyKappa, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HessianOffFeatureBailMultiplier, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HessianMinExpansionsBudget, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HessianExpansionsPerEuclidPx, SmartAppendLineFeaturePointHandler<Polyline, EditablePolyline>.Options.HessianMaxEuclidCells), () => batchSample != null);
		ToolDescriptor toolDescriptor8 = FocusedEditDescriptor(idPrefix + ".GP.DeepConnect", "Connect", "Link", "Click to draw a polyline whose vertices snap to the nearest visible polyline, polygon ring, or topology arc.", router9, orchestrator, new OptionStore(DeepConnectOptions.SnapTolerancePixels), () => deepConnectSupplier != null);
		return new _003C_003Ez__ReadOnlySingleElementList<ToolNode>(GeoprocessingGroup(face, toolDescriptor, toolDescriptor2, toolDescriptor3, toolDescriptor4, toolDescriptor5, toolDescriptor6, toolDescriptor7, toolDescriptor8));
	}

	public static IReadOnlyList<ToolNode> TopologyGeoprocessingTools(VisualContext vc, TopologyEditContext context, string idPrefix = "Topology", Func<IResample<float>> terrainSource = null, Func<RasterDefinition, Memory<float>, bool> batchSample = null, SnapSelector.CandidateSupplier deepConnectSupplier = null)
	{
		ArgumentNullException.ThrowIfNull(vc, "vc");
		ArgumentNullException.ThrowIfNull(context, "context");
		if (!(vc.EditLayer is ToolOrchestrator editor))
		{
			return Array.Empty<ToolNode>();
		}
		EditHandlerRouter router = ToolBuilders.BuildTopologyGpReverseRouter(context);
		EditHandlerRouter router2 = ToolBuilders.BuildTopologyGpSnapRouter(context, terrainSource);
		EditHandlerRouter router3 = ToolBuilders.BuildTopologyGpFilterRouter(context);
		EditHandlerRouter router4 = ToolBuilders.BuildTopologyGpSmoothRouter(context);
		EditHandlerRouter router5 = ToolBuilders.BuildTopologySmartAddRouter(context, batchSample);
		EditHandlerRouter router6 = ToolBuilders.BuildTopologyDeepConnectRouter(context, editor, deepConnectSupplier);
		Func<ToolOrchestrator> orchestrator = () => vc.EditLayer as ToolOrchestrator;
		ToolDescriptor face = FocusedEditDescriptor(idPrefix + ".GP.Reverse", "Reverse", "Refresh", "Click an arc to reverse its internal points (start/end nodes are pinned).", router, orchestrator);
		ToolDescriptor toolDescriptor = FocusedEditDescriptor(idPrefix + ".GP.Snap", "Snap", "Pin", "Click an arc (or drag a closed lasso) to snap its internal points to nearby high or low ground.", router2, orchestrator, new OptionStore(SnapTolerance.SearchRadiusPixels, SnapTolerance.SearchRadiusWorld, SnapPolylineByClickHandler.Options.SampleSpacing, SnapPolylineByClickHandler.Options.SearchMode, SnapPolylineByClickHandler.Options.ElevationMode, SnapPolylineByClickHandler.Options.PointsPerRing, SnapPolylineByClickHandler.Options.Iterations), () => terrainSource?.Invoke() != null);
		ToolDescriptor toolDescriptor2 = FocusedEditDescriptor(idPrefix + ".GP.Filter", "Filter", "Filter", "Click an arc to Douglas-Peucker simplify its internal points. Hover to preview.", router3, orchestrator, new OptionStore(FilterArcHandler.Options.Epsilon));
		ToolDescriptor toolDescriptor3 = FocusedEditDescriptor(idPrefix + ".GP.Smooth", "Smooth", "Smooth", "Click an arc to smooth its internal points (moving average). Hover to preview.", router4, orchestrator, new OptionStore(SmoothArcHandler.Options.WindowSize, SmoothArcHandler.Options.Iterations));
		ToolDescriptor toolDescriptor4 = FocusedEditDescriptor(idPrefix + ".GP.SmartAdd", "Ridge", "AI", "Click to start drawing an arc whose segments follow ridges/valleys between clicks (requires terrain).", router5, orchestrator, new OptionStore(SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.Algorithm, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.DpTolerancePixels, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.MaxSegmentPixels, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.PreviewThrottlePixels, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HighGroundAlpha, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HighGroundBailRatio, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HighGroundSnapRadiusCells, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HessianFrangiBeta, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HessianFeaturePenaltyKappa, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HessianOffFeatureBailMultiplier, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HessianMinExpansionsBudget, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HessianExpansionsPerEuclidPx, SmartAppendLineFeaturePointHandler<ArcPolylineWrapper, EditableArc>.Options.HessianMaxEuclidCells), () => batchSample != null);
		ToolDescriptor toolDescriptor5 = FocusedEditDescriptor(idPrefix + ".GP.DeepConnect", "Connect", "Link", "Click to draw an arc whose vertices snap to the nearest visible polyline, polygon ring, or topology arc.", router6, orchestrator, new OptionStore(DeepConnectOptions.SnapTolerancePixels), () => deepConnectSupplier != null);
		return new _003C_003Ez__ReadOnlySingleElementList<ToolNode>(GeoprocessingGroup(face, toolDescriptor, toolDescriptor2, toolDescriptor3, toolDescriptor4, toolDescriptor5));
	}
}
