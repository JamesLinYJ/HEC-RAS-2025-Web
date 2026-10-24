using System;

namespace Geospatial.Rasters;

public interface IPinnedTileProvider<T> where T : unmanaged, IEquatable<T>
{
	bool TryReadBlockPinned(long idx1D, out ReadOnlyMemory<T> data);

	void ReleaseBlock(long idx1D);
}
