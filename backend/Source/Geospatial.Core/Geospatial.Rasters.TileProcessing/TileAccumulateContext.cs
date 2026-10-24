using System;

namespace Geospatial.Rasters.TileProcessing;

public readonly struct TileAccumulateContext<TPixel, TState> where TPixel : unmanaged, IEquatable<TPixel>
{
	public long TileIndex { get; }

	public RasterDefinition TileDefinition { get; }

	public TPixel? NoDataValue { get; }

	public TState State { get; }

	public TileWithNeighbors<TPixel> Tiles { get; }

	public RasterData<TPixel> Center => Tiles.Center;

	public TileAccumulateContext(long tileIndex, RasterDefinition tileDefinition, TPixel? noDataValue, TState state, TileWithNeighbors<TPixel> tiles)
	{
		TileIndex = tileIndex;
		TileDefinition = tileDefinition;
		NoDataValue = noDataValue;
		State = state;
		Tiles = tiles;
	}
}
