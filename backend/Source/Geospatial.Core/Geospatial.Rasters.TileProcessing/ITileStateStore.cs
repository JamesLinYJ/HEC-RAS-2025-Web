using System;
using System.Collections.Generic;

namespace Geospatial.Rasters.TileProcessing;

public interface ITileStateStore<TState> : IDisposable
{
	IReadOnlyCollection<long> TileIndices { get; }

	TState GetForUpdate(long tileIndex);

	bool TryGetForUpdate(long tileIndex, out TState state);

	bool TryPeek(long tileIndex, out TState state);

	bool Contains(long tileIndex);

	IDisposable BeginScope();
}
