namespace Geospatial.Rasters;

public interface ITiledRasterDefinition : IBlockedRasterDefinition, IRasterDefinition
{
	int TileSize { get; }

	RasterDefinition TiledRasterDefinition { get; }

	RasterDefinition GetTileRasterDefinition(long blockIndex)
	{
		return TiledRasterDefinition.GetCellRasterDefinition(blockIndex, TileSize);
	}

	RasterDefinition GetTileRasterDefinition(Pixel tileIdx)
	{
		return GetTileRasterDefinition(tileIdx.To1D(TiledRasterDefinition.Cols));
	}

	RasterDefinition GetRenderBlockTileDefinition()
	{
		RasterDefinition tiledRasterDefinition = TiledRasterDefinition;
		double cellSizeTiledRasterDefinitionTerm = tiledRasterDefinition.CellSize / (double)TileSize * 0.5;
		return tiledRasterDefinition.Translate(cellSizeTiledRasterDefinitionTerm, 0.0 - cellSizeTiledRasterDefinitionTerm);
	}
}
