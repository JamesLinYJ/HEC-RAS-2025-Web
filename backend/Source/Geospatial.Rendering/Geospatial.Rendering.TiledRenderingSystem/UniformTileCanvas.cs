using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class UniformTileCanvas : ITileCanvas
{
	private static readonly ILogger _logger = LogHelper.GetLogger("UniformTileCanvas");

	public ICanvas Canvas { get; private init; }

	public RasterDefinition CanvasWorldRaster { get; set; }

	public double CellSize => CanvasWorldRaster.CellSize;

	public UniformTileCanvas(ICanvas cnv)
	{
		Canvas = cnv;
	}

	public void SetWindow(Point upperLeft, double screenRasterCellSize)
	{
	}

	public void SetTilePositioningAndScale(RenderTile tile)
	{
		ITileCanvas.SetTilePositioning(CanvasWorldRaster, tile);
	}

	public void RefreshAllTilePositioningAndScales(IList<ActiveTile> activeTiles)
	{
		for (int i = 0; i < activeTiles.Count; i++)
		{
			SetTilePositioningAndScale(activeTiles[i].RenderTile);
		}
	}
}
