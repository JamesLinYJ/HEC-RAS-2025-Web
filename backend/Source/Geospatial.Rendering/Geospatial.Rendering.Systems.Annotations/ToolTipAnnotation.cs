using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.TiledRenderingSystem;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering.Systems.Annotations;

public class ToolTipAnnotation : IMapAnnotation
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ToolTipAnnotation");

	private bool _isDisposed;

	private PyramidedTileManager _map;

	private Func<bool> _isEnabled;

	private Func<IRenderLayer> _getActiveLayer;

	private IMapToolTip _toolTip;

	private List<LayerToolTip> _toolTipTempCache = new List<LayerToolTip>();

	public bool IsDisposed => _isDisposed;

	public ToolTipAnnotation(PyramidedTileManager map, IMapToolTip toolTipControl, Func<IRenderLayer> getActiveLayer, Func<bool> isEnabled = null)
	{
		_map = map;
		_toolTip = toolTipControl;
		_getActiveLayer = getActiveLayer;
		_isEnabled = isEnabled;
		map.AnnotationCanvas.AddChild(toolTipControl);
	}

	public void Update(HitTestArgs hta)
	{
		((IMapAnnotation)this).ValidateNotDisposed();
		Func<bool> isEnabled = _isEnabled;
		if ((isEnabled != null && !isEnabled()) || hta == null)
		{
			_toolTip.SetVisibility(vis: false);
			return;
		}
		object hitTestCache = null;
		if (hta.Window.IsNull())
		{
			return;
		}
		_toolTipTempCache.Clear();
		Point queryPoint = hta.QueryPoint;
		IRenderLayer iRenderLayer = _getActiveLayer?.Invoke();
		foreach (IRenderLayer flattenRenderLayerCollection in FlattenRenderLayerCollections(_map.RenderContext))
		{
			List<MapHover> mapHovers;
			try
			{
				mapHovers = flattenRenderLayerCollection?.MapHover(hta, ref hitTestCache) ?? null;
			}
			catch (Exception ex)
			{
				mapHovers = new List<MapHover>();
				mapHovers.Add(new MapHover(Point.Origin, "Error sampling " + flattenRenderLayerCollection.DisplayName));
				_logger.LogOnce(LogLevel.Warning, "Error in tooltip sampling " + ex.Message);
			}
			if (mapHovers == null || mapHovers.Count <= 0)
			{
				continue;
			}
			foreach (MapHover mapHover in mapHovers)
			{
				if (mapHover != null)
				{
					LayerToolTip layerToolTip = new LayerToolTip
					{
						RenderLayer = flattenRenderLayerCollection,
						ToolTipValue = mapHover.Label
					};
					if (flattenRenderLayerCollection.Equals(iRenderLayer) && _toolTipTempCache.Count != 0)
					{
						_toolTipTempCache.Insert(0, layerToolTip);
					}
					else
					{
						_toolTipTempCache.Add(layerToolTip);
					}
				}
			}
		}
		PlotRequest plotRequest = _map.TryGetRenderedData(queryPoint);
		if (plotRequest != null && plotRequest.RasterQuery.Contains(queryPoint))
		{
			lock (plotRequest.RasterDataLock)
			{
				Dictionary<IRenderLayer, IMemoryOwner<float>> rasterData = plotRequest.RasterData;
				if (rasterData != null && rasterData.Count > 0)
				{
					int index = plotRequest.RasterQuery.CellIndex(queryPoint);
					foreach (KeyValuePair<IRenderLayer, IMemoryOwner<float>> rasterData2 in rasterData)
					{
						try
						{
							float toolTipValue = rasterData2.Value.Memory.Span[index];
							if (toolTipValue != -9999f)
							{
								IRenderLayer key = rasterData2.Key;
								LayerToolTip layerToolTip2 = new LayerToolTip
								{
									RenderLayer = key,
									ToolTipValue = toolTipValue
								};
								if (key.Equals(iRenderLayer) && _toolTipTempCache.Count != 0)
								{
									_toolTipTempCache.Insert(0, layerToolTip2);
								}
								else
								{
									_toolTipTempCache.Add(layerToolTip2);
								}
							}
						}
						catch (Exception exception)
						{
							_logger.LogError(exception, "Potential memory bug with grabbing data from _activeTile");
						}
					}
				}
			}
		}
		if (_toolTipTempCache.Count > 0)
		{
			_toolTip.SetToolTips(_toolTipTempCache);
			_toolTip.SetVisibility(vis: true);
			double actualWidth = _toolTip.ActualWidth;
			if (actualWidth == 0.0 || double.IsNaN(actualWidth))
			{
				actualWidth = 100.0;
			}
			Pixel pixel = _map.ConvertWorldCoordinateToScreenPixel(hta.QueryPoint);
			_toolTip.SetCanvasOffsets((double)pixel.X - actualWidth * 0.5, pixel.Y + 24);
			_map.AnnotationCanvas.SetZIndex(_toolTip, 100000);
		}
		else
		{
			_toolTip.SetVisibility(vis: false);
		}
	}

	public void Dispose()
	{
		((IMapAnnotation)this).ValidateNotDisposed();
		_map.AnnotationCanvas?.RemoveChild(_toolTip);
		_isDisposed = true;
	}

	private IEnumerable<IRenderLayer> FlattenRenderLayerCollections(IRenderLayer rl)
	{
		if (!(rl is IRenderLayerCollection renderLayerCollection))
		{
			yield return rl;
			yield break;
		}
		foreach (IRenderLayer layer in renderLayerCollection.Layers)
		{
			foreach (IRenderLayer flattenRenderLayerCollection in FlattenRenderLayerCollections(layer))
			{
				yield return flattenRenderLayerCollection;
			}
		}
	}
}
