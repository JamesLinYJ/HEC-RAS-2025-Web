using System;

namespace Geospatial.Rasters;

public interface ITileWriter<T> : IRasterWriter<T>, IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
{
	int TileCount => TiledRasterDefinition.CellCount;

	int TileSize { get; }

	RasterDefinition TiledRasterDefinition { get; }

	void WriteTile(int tileIdx, Memory<T> data)
	{
		WriteBlock(tileIdx, data);
	}

	void WriteTile(Pixel tileIdx, Memory<T> data)
	{
		WriteBlock(TiledRasterDefinition.CellIndex(tileIdx), data);
	}

	void WriteTile(int tileRow, int tileCol, Memory<T> data)
	{
		WriteBlock(TiledRasterDefinition.CellIndex(tileRow, tileCol), data);
	}

	RasterDefinition GetTileRaster(int tileIdx)
	{
		return TiledRasterDefinition.GetCellRasterDefinition(tileIdx, TileSize);
	}
}
