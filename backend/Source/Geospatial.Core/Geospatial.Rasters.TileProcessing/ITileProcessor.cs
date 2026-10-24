using System;

namespace Geospatial.Rasters.TileProcessing;

public interface ITileProcessor<TPixel, TState, TResult> where TPixel : unmanaged, IEquatable<TPixel>
{
	ITileStateSerializer<TState> Serializer { get; }

	TState CreateState(long tileIndex, RasterDefinition tileDefinition);

	void Accumulate(in TileAccumulateContext<TPixel, TState> context);

	bool Resolve(in TileResolveContext<TState> context)
	{
		return false;
	}

	TResult Reduce(ITileStateStore<TState> states, ITiledRasterDefinition tiling);
}
