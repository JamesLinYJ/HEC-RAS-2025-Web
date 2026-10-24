using System;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Tools;
using Geospatial.Rendering.TiledRenderingSystem;
using Utility.Systems;

namespace Visual.Mapping.Observables;

public static class DefaultMapToolDescriptors
{
	public const string PanId = "Map.Pan";

	public const string SelectExtentId = "Map.SelectExtent";

	public const string SelectPathId = "Map.SelectPath";

	public const string SelectLassoId = "Map.SelectLasso";

	public const string MeasureAreaId = "Map.MeasureArea";

	public const string MeasureLengthId = "Map.MeasureLength";

	public static ToolDescriptor MeasureArea { get; } = new ToolDescriptor("Map.MeasureArea", "Area", "Area", DefaultMapTools.CreateMeasureAreaTool)
	{
		Description = "Measure the area of a polygon you draw on the map."
	};

	public static ToolDescriptor CreatePan(Func<IVectorRenderLayer> getActiveLayer)
	{
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		return new ToolDescriptor("Map.Pan", "Pan", "Hand", (PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreatePanTool(manager, keyState, getActiveLayer))
		{
			Description = "Pan around the map by clicking and dragging."
		};
	}

	public static ToolDescriptor CreateSelectExtent(Func<IVectorRenderLayer> getActiveLayer)
	{
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		OptionStore options = new OptionStore(LassoSelectionHandler.Options.HitMode);
		return CreateSelectExtent((PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreateExtentSelectionTool(manager, keyState, getActiveLayer, options), options);
	}

	public static ToolDescriptor CreateSelectExtent(Func<PyramidedTileManager, Func<KeyCode, bool>, MapTool> build, OptionStore options = null)
	{
		ArgumentNullException.ThrowIfNull(build, "build");
		return new ToolDescriptor("Map.SelectExtent", "Extent", "Selection", build)
		{
			Description = "Drag a rectangle to select features.",
			Options = options
		};
	}

	public static ToolDescriptor CreateSelectPath(Func<IVectorRenderLayer> getActiveLayer)
	{
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		return CreateSelectPath((PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreatePathSelectionTool(manager, keyState, getActiveLayer));
	}

	public static ToolDescriptor CreateSelectPath(Func<PyramidedTileManager, Func<KeyCode, bool>, MapTool> build, OptionStore options = null)
	{
		ArgumentNullException.ThrowIfNull(build, "build");
		return new ToolDescriptor("Map.SelectPath", "Path", "PolyLine", build)
		{
			Description = "Draw a freeform path; features the path crosses are selected.",
			Options = options
		};
	}

	public static ToolDescriptor CreateSelectLasso(Func<IVectorRenderLayer> getActiveLayer)
	{
		ArgumentNullException.ThrowIfNull(getActiveLayer, "getActiveLayer");
		OptionStore options = new OptionStore(LassoSelectionHandler.Options.HitMode);
		return CreateSelectLasso((PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreateLassoSelectionTool(manager, keyState, getActiveLayer, options), options);
	}

	public static ToolDescriptor CreateSelectLasso(Func<PyramidedTileManager, Func<KeyCode, bool>, MapTool> build, OptionStore options = null)
	{
		ArgumentNullException.ThrowIfNull(build, "build");
		return new ToolDescriptor("Map.SelectLasso", "Lasso", "Lasso", build)
		{
			Description = "Draw a freeform closed shape; features inside are selected.",
			Options = options
		};
	}

	public static ToolDescriptor CreateMeasureLength(MeasureSlopeDelegate slope)
	{
		return new ToolDescriptor("Map.MeasureLength", "Length", "Ruler", (PyramidedTileManager manager, Func<KeyCode, bool> keyState) => DefaultMapTools.CreateMeasureLengthTool(manager, keyState, slope))
		{
			Description = "Measure the length of a polyline you draw on the map."
		};
	}
}
