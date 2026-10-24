using System;

namespace Geospatial.Rasters;

public interface IRasterProvider<T> : IBlockedRasterDefinition, IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
{
	bool ReadBlock(long blockIndex, Memory<T> block);
}
