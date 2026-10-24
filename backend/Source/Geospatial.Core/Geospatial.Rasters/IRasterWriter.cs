using System;

namespace Geospatial.Rasters;

public interface IRasterWriter<T> : IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
{
	bool CanWriteTilesInParallel { get; }

	BlockSize BlockSize { get; }

	void WriteBlock(int blockIndex, Memory<T> block);
}
