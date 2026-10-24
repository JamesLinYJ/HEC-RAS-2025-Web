using System;
using System.Collections.Generic;
using System.Diagnostics;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Memory;

namespace H5Assist.Caching;

public class H5RowCache<T> : IDisposable where T : unmanaged
{
	private readonly ILogger _logger = LogHelper.GetLogger("H5RowCache");

	private readonly NativeResourceWrapper<H5Reader> _wrapper;

	private readonly Dictionary<string, DatasetParameters> _parameters = new Dictionary<string, DatasetParameters>();

	private readonly ArrayCacheManager<ChunkDescriptor, T> _chunkCache = new ArrayCacheManager<ChunkDescriptor, T>();

	public H5RowCache(H5Reader h5r)
	{
		_wrapper = new NativeResourceWrapper<H5Reader>(h5r);
	}

	public H5RowCache(string filename, bool keepHandleOpen = false)
	{
		_wrapper = (keepHandleOpen ? H5ResourceWrapper.KeepOpen(filename) : H5ResourceWrapper.OpenOnDemand(filename));
	}

	public void UseReader(Action<H5Reader> readAction)
	{
		_wrapper.UseResource(readAction);
	}

	public K UseReader<K>(Func<H5Reader, K> readAction)
	{
		return _wrapper.UseResource(readAction);
	}

	private bool TryGetDatasetParams(string dset, out DatasetParameters ds)
	{
		lock (_parameters)
		{
			return _parameters.TryGetValue(dset, out ds);
		}
	}

	private void SetDatasetParameters(DatasetParameters ds)
	{
		lock (_parameters)
		{
			_parameters[ds.Name] = ds;
		}
	}

	public void GetRow(string dataset, int readRow, Memory<T> outBuffer)
	{
		if (TryGetDatasetParams(dataset, out var dsParams))
		{
			ReadChunk(dsParams, null, readRow, outBuffer);
			return;
		}
		_wrapper.UseResource((H5Reader hr) =>
		{
			dsParams = hr.GetDatasetParameters(dataset);
			AssertRowChunked(dsParams);
			SetDatasetParameters(dsParams);
			ReadChunk(dsParams, hr, readRow, outBuffer);
		});
	}

	public OperationResult TryGetRow(string dataset, int readRow, Memory<T> outBuffer)
	{
		if (TryGetDatasetParams(dataset, out var ds))
		{
			ReadChunk(ds, null, readRow, outBuffer);
			return true;
		}
		return _wrapper.UseResource((Func<H5Reader, OperationResult>)((H5Reader hr) =>
		{
			bool datasetParameters = hr.TryGetDatasetParameters(dataset, out var para);
			if (!datasetParameters)
			{
				return datasetParameters;
			}
			AssertRowChunked(para);
			SetDatasetParameters(para);
			ReadChunk(para, hr, readRow, outBuffer);
			return true;
		}));
	}

	private void ReadChunk(DatasetParameters dsParams, H5Reader hr, int readRow, Memory<T> outBuffer)
	{
		string dataset = dsParams.Name;
		int chunkIdx = ChunkIndex(dsParams.ChunkDimensions, readRow);
		ChunkDescriptor key = new ChunkDescriptor(dataset, chunkIdx, 0L);
		_chunkCache.BuildOrUseExisting(key, (int)dsParams.ElementsPerChunk, (Memory<T> buf) =>
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (hr == null)
			{
				_wrapper.UseResource((H5Reader h5Reader) =>
				{
					using H5DatasetID dataset3 = h5Reader.OpenDataset(dataset);
					h5Reader.ReadChunkDirect(dataset3, new int[2] { chunkIdx, 0 }, buf);
				});
			}
			else
			{
				using H5DatasetID dataset2 = hr.OpenDataset(dataset);
				hr.ReadChunkDirect(dataset2, new int[2] { chunkIdx, 0 }, buf);
			}
			stopwatch.Stop();
			_logger.LogDebug("{Dataset} chunk read in {Duration} ms.", dataset, stopwatch.Elapsed.TotalMilliseconds);
		}, (Memory<T> chunkBuf) =>
		{
			int dimension = (int)dsParams.Dimensions[1];
			int chunkDimension = (int)dsParams.ChunkDimensions[0];
			int chunkIdxTimesChunkDimension = chunkIdx * chunkDimension;
			int start = (readRow - chunkIdxTimesChunkDimension) * dimension;
			chunkBuf.Slice(start, dimension).CopyTo(outBuffer);
		});
	}

	private void AssertRowChunked(DatasetParameters dsParams)
	{
		if (dsParams.Dimensions[1] != dsParams.ChunkDimensions[1])
		{
			string message = dsParams.Name + " is not chunked by rows.";
			_logger.LogError(message);
			throw new Exception(message);
		}
	}

	private static int ChunkIndex(long[] chunkDims, int rowIdx)
	{
		return (int)Math.Floor((double)rowIdx / (double)chunkDims[0]);
	}

	private static bool IsOOB(long[] dsetDims, int rowIdx)
	{
		if (rowIdx < dsetDims[0])
		{
			return rowIdx < 0;
		}
		return true;
	}

	public void Dispose()
	{
		((IDisposable)_wrapper).Dispose();
	}
}
