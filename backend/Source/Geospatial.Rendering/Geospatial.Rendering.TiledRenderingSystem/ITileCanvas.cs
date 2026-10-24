using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.TiledRenderingSystem;

public interface ITileCanvas
{
	ICanvas Canvas { get; }

	double CellSize { get; }

	RasterDefinition CanvasWorldRaster { get; }

	void SetWindow(Point upperLeft, double screenRasterCellSize);

	void SetTilePositioningAndScale(RenderTile tile);

	void RefreshAllTilePositioningAndScales(IList<ActiveTile> activeTiles);

	static void SetTilePositioning(RasterDefinition worldRaster, RenderTile tile, double rasterTranslationX = 0.0, double rasterTranslationY = 0.0, double rasterScale = 1.0, double tileScale = 1.0)
	{
		double leftWorldRasterTerm = worldRaster.Left / worldRaster.CellSize + rasterTranslationX / rasterScale;
		double topWorldRasterTerm = worldRaster.Top / worldRaster.CellSize - rasterTranslationY / rasterScale;
		double a = 256.0 * (double)tile.TileColumn * tileScale - leftWorldRasterTerm;
		double a2 = 256.0 * (double)tile.TileRow * tileScale + topWorldRasterTerm;
		a = Math.Round(a);
		a2 = Math.Round(a2);
		tile.Image.SetCanvasOffsets(a, a2);
	}
}
