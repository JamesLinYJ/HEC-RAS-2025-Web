using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal sealed class VirtualTimeSeriesRasterProvider<T> : ITimeSeriesRasterProvider<T>, IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, IDiscreteTimesteps, ICacheEviction where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandRasterProvider<T> _banded;

	public IReadOnlyList<DateTime> Times { get; }

	public RasterDefinition RasterDefinition => _banded.RasterDefinition;

	public Extent ValidExtent => _banded.ValidExtent;

	public T? NoDataValue => _banded.NoDataValue;

	public BlockSize BlockSize => _banded.BlockSize;

	public int ProfileCount => _banded.ProfileCount;

	public BlockSize VerticallyChunkedBlockSize => _banded.VerticallyChunkedBlockSize;

	public VirtualTimeSeriesRasterProvider(IMultiBandRasterProvider<T> banded, IReadOnlyList<DateTime> times)
	{
		_banded = banded ?? throw new ArgumentNullException("banded");
		Times = times ?? throw new ArgumentNullException("times");
	}

	public bool ReadVerticalBlock(int verticalBlockIndex, Memory<T> block)
	{
		return _banded.ReadVerticalBlock(verticalBlockIndex, block);
	}

	public bool ReadBlock(int profileIndex, long blockIndex, Memory<T> block)
	{
		return _banded.ReadBlock(profileIndex, blockIndex, block);
	}

	public void EvictCache()
	{
		(_banded as ICacheEviction)?.EvictCache();
	}

	public IDisposable TryPauseCache()
	{
		return (_banded as ICacheEviction)?.TryPauseCache() ?? NoOpDisposable.Instance;
	}
}
