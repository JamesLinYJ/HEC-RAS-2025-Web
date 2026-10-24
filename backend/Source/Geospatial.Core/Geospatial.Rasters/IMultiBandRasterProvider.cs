using System;

namespace Geospatial.Rasters;

public interface IMultiBandRasterProvider<T> : IBlockedRasterDefinition, IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
{
	int ProfileCount { get; }

	BlockSize VerticallyChunkedBlockSize { get; }

	bool ReadBlock(int profileIndex, long blockIndex, Memory<T> block);

	bool ReadVerticalBlock(int verticalBlockIndex, Memory<T> block);
}
