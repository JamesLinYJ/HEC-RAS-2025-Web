using System;
using Geospatial.Rasters;

namespace Geospatial;

public class RasterTiledFloodFillHelper<T> : TiledFloodFillHelper<Memory<T>, T> where T : unmanaged, IEquatable<T>
{
	public delegate void LoadTileData(int tileIndex, Memory<T> buffer);

	public delegate bool CellTraversalTest(T fromValue, T toValue, bool diagonal);

	public delegate bool CellIncludeTest(T localValue);

	public RasterTiledFloodFillHelper(int tileRows, int tileCols, int tileSize, RasterCellNeighborSet neighbors, LoadTileData loadTile, CellTraversalTest traversalTest)
		: base(tileRows, tileCols, tileSize, neighbors, (AllocTile)(() => new T[tileSize * tileSize]), (InitTile)((int tileIndex, Memory<T> buffer) =>
		{
			loadTile(tileIndex, buffer);
		}), (FromToTest)((Memory<T> tile, int from, int to) => traversalTest(tile.Span[from], tile.Span[to], IsDiagonal(from, to, tileSize))), (GetBoundaryContext)((Memory<T> tile, int idx) => tile.Span[idx]), (CrossTileTest)((T sourceValue, Memory<T> targetTile, int targetIdx, bool diagonal) => traversalTest(sourceValue, targetTile.Span[targetIdx], diagonal)), (CleanupTile)null)
	{
	}

	public RasterTiledFloodFillHelper(int tileRows, int tileCols, int tileSize, RasterCellNeighborSet neighbors, LoadTileData loadTile, CellIncludeTest includeTest)
		: base(tileRows, tileCols, tileSize, neighbors, (AllocTile)(() => new T[tileSize * tileSize]), (InitTile)((int tileIndex, Memory<T> buffer) =>
		{
			loadTile(tileIndex, buffer);
		}), (IncludeTest)((Memory<T> tile, int idx) => includeTest(tile.Span[idx])), (CleanupTile)null)
	{
	}

	private static bool IsDiagonal(int fromLocalIndex, int toLocalIndex, int tileSize)
	{
		if (fromLocalIndex / tileSize != toLocalIndex / tileSize)
		{
			return fromLocalIndex % tileSize != toLocalIndex % tileSize;
		}
		return false;
	}
}
