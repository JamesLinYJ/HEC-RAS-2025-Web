using Geospatial.Vectors;

namespace Geospatial.Rasters.TileProcessing;

public readonly struct TileResolveContext<TState>
{
	private readonly ITileStateStore<TState> _store;

	public long TileIndex { get; }

	public Pixel TileXY { get; }

	public RasterDefinition TileDefinition { get; }

	public TState State { get; }

	public ITiledRasterDefinition Tiling { get; }

	public TileResolveContext(long tileIndex, Pixel tileXY, RasterDefinition tileDefinition, TState state, ITiledRasterDefinition tiling, ITileStateStore<TState> store)
	{
		TileIndex = tileIndex;
		TileXY = tileXY;
		TileDefinition = tileDefinition;
		State = state;
		Tiling = tiling;
		_store = store;
	}

	public bool TryGetTileAt(Point worldPoint, out long tileIndex, out TState state)
	{
		tileIndex = -1L;
		state = default;
		RasterDefinition tiledRasterDefinition = Tiling.TiledRasterDefinition;
		Pixel i2d = tiledRasterDefinition.CellIndex2D(worldPoint);
		if (!tiledRasterDefinition.InRange(i2d))
		{
			return false;
		}
		long tileIndex2 = tiledRasterDefinition.CellIndexLong(i2d.Row, i2d.Column);
		if (tileIndex2 == TileIndex)
		{
			return false;
		}
		tileIndex = tileIndex2;
		return _store.TryGetForUpdate(tileIndex2, out state);
	}

	public bool TryGetNeighbor(int deltaColumn, int deltaRow, out long tileIndex, out TState state)
	{
		tileIndex = -1L;
		state = default;
		RasterDefinition tiledRasterDefinition = Tiling.TiledRasterDefinition;
		Pixel i2d = new Pixel(TileXY.Row + deltaRow, TileXY.Column + deltaColumn);
		if (!tiledRasterDefinition.InRange(i2d))
		{
			return false;
		}
		tileIndex = tiledRasterDefinition.CellIndexLong(i2d.Row, i2d.Column);
		return _store.TryGetForUpdate(tileIndex, out state);
	}
}
