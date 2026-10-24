using System;
using System.Diagnostics;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.IO;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class H5MultiBandRasterProvider<T> : IDisposable, IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
{
	public static class MessageCodes
	{
		public static readonly MessageClass CouldNotFindOrParseRequiredData = new MessageClass("GenericParseErrors_RequiredParameterMissing", MessageSeverity.Error, "{FileName} is missing required data on {DatasetName} ({ExpectedData}.");

		public static Message GetCouldNotFindOrParseRequiredData(string filename, string datasetName, string expectedData)
		{
			return CouldNotFindOrParseRequiredData.Create(filename, datasetName, expectedData);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("H5BandedRasterDataSource");

	protected NativeResourceWrapper<H5Reader> _hwrap;

	protected NativeResourceWrapper<H5Reader, H5DatasetID> _hzDset;

	protected NativeResourceWrapper<H5Reader, H5DatasetID> _vertDset;

	private bool _isDisposed;

	private ArrayCacheManager<(int, int), T> _cache;

	protected bool _isTiled;

	public int ProfileCount { get; private set; }

	public BlockSize BlockSize { get; private set; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue { get; private set; }

	public RasterDefinition RasterDefinition { get; private set; }

	public BlockSize VerticallyChunkedBlockSize { get; private set; }

	protected H5MultiBandRasterProvider()
	{
	}

	public static OperationResult TryOpen(NativeResourceWrapper<H5Reader> hwrap, string datasetName, out H5MultiBandRasterProvider<T> result)
	{
		if (hwrap == null || hwrap.IsDisposed)
		{
			throw new ArgumentException("hwrap is null or disposed.");
		}
		return TryInitBandedInternal(hwrap, datasetName, out result);
	}

	public static H5MultiBandRasterProvider<T> Open(NativeResourceWrapper<H5Reader> hwrap, string datasetName)
	{
		if (hwrap == null || hwrap.IsDisposed)
		{
			throw new ArgumentException("hwrap is null or disposed.");
		}
		if (!TryInitBandedInternal(hwrap, datasetName, out var result).Result)
		{
			throw new Exception("Could not open target dataset.");
		}
		return result;
	}

	protected static OperationResult TryInitBandedInternal(NativeResourceWrapper<H5Reader> hwrap, string datasetName, out H5MultiBandRasterProvider<T> result)
	{
		result = new H5MultiBandRasterProvider<T>();
		return TryInitBandedInternal(result, hwrap, datasetName);
	}

	protected static OperationResult TryInitBandedInternal(H5MultiBandRasterProvider<T> h5MultiBandRasterProvider, NativeResourceWrapper<H5Reader> hwrap, string datasetName)
	{
		NativeResourceWrapper<H5Reader, H5DatasetID> hzDset = new NativeResourceWrapper<H5Reader, H5DatasetID>((H5Reader hr) => hr.OpenDataset(datasetName));
		h5MultiBandRasterProvider._hzDset = hzDset;
		h5MultiBandRasterProvider._hwrap = hwrap;
		string vertDatasetName = H5RasterWriter.GetDefaultVerticalDatasetName(datasetName);
		h5MultiBandRasterProvider._vertDset = new NativeResourceWrapper<H5Reader, H5DatasetID>((H5Reader hr) => hr.OpenDataset(vertDatasetName));
		OperationResult operationResult = hwrap.UseResource((H5Reader hr) =>
		{
			string fn = hr.Filename;
			return (!hr.ExistsAsDataset(datasetName)) ? OperationResult.Fail(H5Reader.MessageCodes.GetDatasetNotFound(fn, datasetName)) : hzDset.UseResource(hr, (H5DatasetID dsetId) =>
			{
				h5MultiBandRasterProvider.RasterDefinition = HDF5VectorReader.TryReadRasterDefinition(hr, datasetName);
				if (h5MultiBandRasterProvider.RasterDefinition.IsNull())
				{
					return OperationResult.Fail(MessageCodes.GetCouldNotFindOrParseRequiredData(fn, datasetName, "RasterDefinition"));
				}
				if (H5RasterWriter.IsTiledConfig(hr, datasetName))
				{
					h5MultiBandRasterProvider.BlockSize = new BlockSize(256, 256);
					h5MultiBandRasterProvider._isTiled = true;
				}
				else
				{
					h5MultiBandRasterProvider.BlockSize = h5MultiBandRasterProvider.RasterDefinition.FullBlockSize();
					h5MultiBandRasterProvider._isTiled = false;
				}
				if (hr.TryReadAttributeScalar(datasetName, "NoData", out T noDataValue))
				{
					h5MultiBandRasterProvider.NoDataValue = noDataValue;
				}
				else
				{
					h5MultiBandRasterProvider.NoDataValue = null;
				}
				long[] dims = hr.GetDatasetDimensions(datasetName);
				if (dims.Length != 2)
				{
					return OperationResult.Fail(H5Reader.MessageCodes.GetDatasetIncorrectRank(fn, datasetName, 2, dims.Length));
				}
				h5MultiBandRasterProvider.ProfileCount = (int)dims[0];
				if (hr.ExistsAsDataset(vertDatasetName))
				{
					OperationResult operationResult2 = h5MultiBandRasterProvider._vertDset.UseResource(hr, (H5DatasetID vertId) =>
					{
						if (hr.GetDatasetDimensions(vertDatasetName).Length != 2)
						{
							return OperationResult.Fail(H5Reader.MessageCodes.GetDatasetIncorrectRank(fn, vertDatasetName, 2, dims.Length));
						}
						long[] chunkSize = hr.GetChunkSize(vertDatasetName);
						h5MultiBandRasterProvider.VerticallyChunkedBlockSize = new BlockSize((int)chunkSize[0], (int)chunkSize[1]);
						return OperationResult.Success();
					});
					if (!operationResult2.Result)
					{
						return operationResult2;
					}
				}
				else
				{
					h5MultiBandRasterProvider.VerticallyChunkedBlockSize = BlockSize.NullBlockSize;
				}
				return OperationResult.Success();
			});
		});
		if (operationResult.Result)
		{
			h5MultiBandRasterProvider._cache = new ArrayCacheManager<(int, int), T>();
			h5MultiBandRasterProvider._cache.CacheManager.MaxCacheSizeBytes = 134217728;
		}
		return operationResult;
	}

	public bool ReadBlock(int profileIndex, long blockIndex, Memory<T> block)
	{
		if (!_isTiled && blockIndex != 0L)
		{
			throw new ArgumentException("blockIndex");
		}
		_cache.BuildOrUseExisting((profileIndex, (int)blockIndex), BlockSize.PixelCount, (Memory<T> buf) =>
		{
			_hwrap.UseResource((H5Reader hr) =>
			{
				_hzDset.UseResource(hr, (H5DatasetID ds) =>
				{
					if (_isTiled)
					{
						long blockIndexTimesPixelCount = blockIndex * BlockSize.PixelCount;
						hr.ReadPartialRow(ds, profileIndex, (int)blockIndexTimesPixelCount, buf);
					}
					else
					{
						hr.ReadRow(ds, profileIndex, buf);
					}
				});
			});
		}, (Memory<T> buf) =>
		{
			buf.CopyTo(block);
		});
		return true;
	}

	public bool ReadVerticalBlock(int verticalBlockIndex, Memory<T> block)
	{
		if (VerticallyChunkedBlockSize.IsNull())
		{
			throw new Exception("This file does not have a vertically chunked dataset.");
		}
		throw new NotImplementedException();
	}

	private string GetDebuggerDisplay()
	{
		return "(" + ProfileCount + " Profiles)";
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			_vertDset?.Dispose();
			_hzDset?.Dispose();
			_hwrap?.Dispose();
			_isDisposed = true;
		}
	}

	~H5MultiBandRasterProvider()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
