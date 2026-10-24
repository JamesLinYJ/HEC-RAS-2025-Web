using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class MemoryTileProvider<T> : ITileReaderWriter<T>, ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition, ITileWriter<T>, IRasterWriter<T> where T : unmanaged, IEquatable<T>
{
	public T? NoDataValue { get; }

	public int TileSize { get; private set; }

	public bool FillMissingTilesWithNoData { get; set; } = true;

	public Memory<T>[,] Tiles { get; }

	public RasterDefinition TiledRasterDefinition { get; }

	public RasterDefinition RasterDefinition { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public int TileCount => TiledRasterDefinition.CellCount;

	public BlockSize BlockSize => new BlockSize(TileSize, TileSize);

	public bool CanWriteTilesInParallel => true;

	public Memory<T> Tile(int tIdx)
	{
		return Tiles[tIdx / TiledRasterDefinition.Rows, tIdx % TiledRasterDefinition.Cols];
	}

	public MemoryTileProvider(RasterDefinition worldRaster, int tileSize = 256, T? noDataValue = null)
	{
		RasterDefinition = worldRaster;
		TiledRasterDefinition = worldRaster.TiledRaster(tileSize);
		Tiles = new Memory<T>[TiledRasterDefinition.Rows, TiledRasterDefinition.Cols];
		TileSize = tileSize;
		NoDataValue = noDataValue;
	}

	public void SetTileBuffer(int row, int col, T[] data)
	{
		if (data == null || data.Length != TileSize * TileSize)
		{
			throw new ArgumentException("Tile/Data size mismatch");
		}
		if (!TiledRasterDefinition.InRange(row, col))
		{
			throw new ArgumentException("Tile index out of bounds");
		}
		Tiles[row, col] = data;
	}

	public void WriteBlock(int blockIndex, Memory<T> block)
	{
		T[] blockArray = new T[TileSize * TileSize];
		block.CopyTo(blockArray);
		SetTileBuffer(blockIndex / TiledRasterDefinition.Cols, blockIndex % TiledRasterDefinition.Cols, blockArray);
	}

	public RasterDefinition GetTileRasterDefinition(Pixel tile)
	{
		return TiledRasterDefinition.GetCellRasterDefinition(tile.Row, tile.Column, TileSize);
	}

	public bool ReadBlock(long tileIndex, Memory<T> data)
	{
		if (data.Length != TileSize * TileSize)
		{
			throw new ArgumentException();
		}
		Pixel pixel = TiledRasterDefinition.CellIndex2D(tileIndex);
		Memory<T> buffer = Tiles[pixel.Row, pixel.Column];
		if (buffer.IsEmpty)
		{
			data.Span.Fill(NoDataValue.GetValueOrDefault());
			return false;
		}
		buffer.CopyTo(data);
		return true;
	}
}
