using System;

namespace Geospatial.Rasters;

public interface IMultiBandRasterWriter<T> : IBlockedRasterDefinition, IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
{
	int ProfileCount { get; }

	bool CanWriteTilesInParallel { get; }

	void WriteBlock(int profileIndex, int blockIndex, Memory<T> block);

	void Flush();
}
