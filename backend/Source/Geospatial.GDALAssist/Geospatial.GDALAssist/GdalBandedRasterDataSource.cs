using System;
using System.Collections.Concurrent;
using Geospatial.GDALAssist.Source;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using Utility;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.GDALAssist;

public class GdalBandedRasterDataSource<T> : IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ICacheEviction, IDisposable where T : unmanaged, IEquatable<T>
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GdalBandedRasterDataSource");

	private readonly GDALRaster _gdr;

	private readonly NativeResourceMode _resourceMode;

	private bool _isDisposed;

	private object _readLock = new object();

	private readonly string _connectionString;

	private Pool<GDALRaster> _readPool;

	private readonly bool _allowParallelReads;

	private readonly ConcurrentDictionary<int, byte> _failedBands = new ConcurrentDictionary<int, byte>();

	public bool CanReadBlocksInParallel => _readPool != null;

	public ReadFailureMode ReadFailureMode { get; set; }

	public BlockSize BlockSize { get; private set; }

	public int ProfileCount { get; private set; }

	public BlockSize VerticallyChunkedBlockSize => BlockSize.NullBlockSize;

	public RasterDefinition RasterDefinition { get; private set; }

	public Extent ValidExtent { get; private set; }

	public T? NoDataValue { get; set; }

	public GdalBandedRasterDataSource(string connectionString, bool allowParallelReads = false)
	{
		_gdr = new GDALRaster(connectionString);
		_resourceMode = NativeResourceMode.KeepOpen;
		_connectionString = connectionString;
		_allowParallelReads = allowParallelReads;
		Init();
	}

	public GdalBandedRasterDataSource(GDALRaster gdr, bool allowParallelReads = false)
	{
		_gdr = gdr;
		_resourceMode = NativeResourceMode.OnLoan;
		_connectionString = gdr.Filename;
		_allowParallelReads = allowParallelReads;
		Init();
	}

	private void Init()
	{
		ProfileCount = _gdr.GetBandCount();
		_gdr.GetBlockSize(1, out var blockXSize, out var blockYSize);
		BlockSize = new BlockSize(blockYSize, blockXSize);
		if (_gdr.TryGetNoData<T>(1, out var ndv))
		{
			NoDataValue = ndv;
		}
		else
		{
			NoDataValue = null;
		}
		RasterDefinition = _gdr.GetNativeRaster();
		ValidExtent = RasterDefinition.Extent;
		if (_allowParallelReads && !string.IsNullOrEmpty(_connectionString))
		{
			_readPool = new Pool<GDALRaster>(() => new GDALRaster(_connectionString, useShared: false));
		}
	}

	public bool ReadBlock(int profileIndex, long blockIndex, Memory<T> block)
	{
		RasterDefinition blockRaster = RasterDefinition.GetBlockRaster(BlockSize, (int)blockIndex);
		if (ReadFailureMode == ReadFailureMode.FillNoData && _failedBands.ContainsKey(profileIndex))
		{
			FillNoData(block);
			return false;
		}
		try
		{
			PixelRange validDataRegion;
			if (_readPool != null)
			{
				using PoolHandle<GDALRaster> poolHandle = _readPool.Acquire();
				poolHandle.Data.ReadBand(profileIndex + 1, blockRaster, block, out validDataRegion);
			}
			else
			{
				lock (_readLock)
				{
					_gdr.ReadBand(profileIndex + 1, blockRaster, block, out validDataRegion);
				}
			}
		}
		catch (Exception exception) when (ReadFailureMode == ReadFailureMode.FillNoData)
		{
			FillNoData(block);
			Gdal.ErrorReset();
			if (_failedBands.TryAdd(profileIndex, 0))
			{
				_logger.LogWarning(exception, "Band {Band} could not be read; filling NoData.", profileIndex + 1);
			}
			return false;
		}
		return true;
	}

	private void FillNoData(Memory<T> block)
	{
		if (NoDataValue.HasValue)
		{
			block.Span.Fill(NoDataValue.Value);
		}
		else
		{
			block.Span.Clear();
		}
	}

	public bool ReadVerticalBlock(int verticalBlockIndex, Memory<T> block)
	{
		throw new NotImplementedException();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing)
		{
			_readPool?.ClearAll((GDALRaster gdr) =>
			{
				gdr.Dispose();
			});
			if (_resourceMode == NativeResourceMode.KeepOpen)
			{
				_gdr?.Dispose();
			}
		}
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public void EvictCache()
	{
		_gdr.DebugRefresh();
		_readPool?.ClearAll((GDALRaster gdr) =>
		{
			gdr.Dispose();
		});
		_failedBands.Clear();
	}

	public IDisposable TryPauseCache()
	{
		return NoOpDisposable.Instance;
	}
}
