using System;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;
using Utility.Threading;

namespace Geospatial.Rasters;

internal class CachedMultiBandRasterProvider<T> : IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ICacheEviction, IDisposable where T : unmanaged, IEquatable<T>
{
	private static readonly ILogger _logger = LogHelper.GetLogger("CachedMultiBandRasterProvider");

	private readonly IMultiBandRasterProvider<T> _inner;

	private readonly IArrayCacheManager<BlockKey, T> _cache;

	private readonly int _blockPixelCount;

	private volatile bool _singlePass;

	public RasterDefinition RasterDefinition => _inner.RasterDefinition;

	public Extent ValidExtent => _inner.ValidExtent;

	public T? NoDataValue => _inner.NoDataValue;

	public BlockSize BlockSize => _inner.BlockSize;

	public int ProfileCount => _inner.ProfileCount;

	public BlockSize VerticallyChunkedBlockSize => _inner.VerticallyChunkedBlockSize;

	public CachedMultiBandRasterProvider(IMultiBandRasterProvider<T> inner, IArrayCacheManager<BlockKey, T> cache)
	{
		_inner = inner ?? throw new ArgumentNullException("inner");
		_blockPixelCount = _inner.BlockSize.PixelCount;
		_cache = cache ?? throw new ArgumentNullException("cache");
	}

	public bool ReadVerticalBlock(int verticalBlockIndex, Memory<T> block)
	{
		return _inner.ReadVerticalBlock(verticalBlockIndex, block);
	}

	public bool ReadBlock(int profileIndex, long blockIndex, Memory<T> block)
	{
		if (_singlePass)
		{
			return _inner.ReadBlock(profileIndex, blockIndex, block);
		}
		bool success = false;
		_cache.BuildOrUseExisting(new BlockKey(profileIndex, blockIndex), _blockPixelCount, (Memory<T> buf) => _inner.ReadBlock(profileIndex, blockIndex, buf), (Memory<T> buf, bool ok) =>
		{
			if (ok)
			{
				buf.CopyTo(block);
			}
			success = ok;
		});
		return success;
	}

	public void EvictCache()
	{
		_cache.ForceEvictAll();
		(_inner as ICacheEviction)?.EvictCache();
	}

	public IDisposable TryPauseCache()
	{
		IDisposable disposable = (_inner as ICacheEviction)?.TryPauseCache() ?? NoOpDisposable.Instance;
		IDisposable disposable2 = (_cache as IPausablePrefetch)?.PauseWarming() ?? NoOpDisposable.Instance;
		_singlePass = true;
		_cache.ForceEvictAll();
		return new DisposableScope(() =>
		{
			_singlePass = false;
		}, disposable2, disposable);
	}

	public void Dispose()
	{
		(_inner as IDisposable)?.Dispose();
	}
}
