using System;

namespace Geospatial.Rasters.TileProcessing;

public interface ITileStateBackingStore : IDisposable
{
	void Write(long tileIndex, ReadOnlySpan<byte> data);

	bool TryRead(long tileIndex, out byte[] data);

	bool Contains(long tileIndex);

	void Remove(long tileIndex);
}
