using System;
using System.Collections.Generic;
using System.Diagnostics;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Memory;

namespace H5Assist.Caching;

public class H5ColumnCache<T> : IDisposable where T : unmanaged
{
	private readonly ILogger _logger = LogHelper.GetLogger("H5ColumnCache");

	private readonly NativeResourceWrapper<H5Reader> _wrapper;

	private readonly Dictionary<string, DatasetParameters> _parameters = new Dictionary<string, DatasetParameters>();

	private readonly ArrayCacheManager<ChunkDescriptor, T> _chunkCache = new ArrayCacheManager<ChunkDescriptor, T>();

	public H5ColumnCache(H5Reader h5r)
	{
		_wrapper = new NativeResourceWrapper<H5Reader>(h5r);
	}

	public H5ColumnCache(string filename, bool keepHandleOpen = false)
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

	public void GetColumn(string dataset, int readCol, Memory<T> outBuffer)
	{
		if (TryGetDatasetParams(dataset, out var dsParams))
		{
			ReadChunk(dsParams, null, readCol, outBuffer);
			return;
		}
		_wrapper.UseResource((H5Reader hr) =>
		{
			dsParams = hr.GetDatasetParameters(dataset);
			AssertColChunked(dsParams);
			SetDatasetParameters(dsParams);
			ReadChunk(dsParams, hr, readCol, outBuffer);
		});
	}

	private void ReadChunk(DatasetParameters dsParams, H5Reader hr, int readCol, Memory<T> outBuffer)
	{
		string dataset = dsParams.Name;
		int chunkIdx = ChunkIndex(dsParams.ChunkDimensions, readCol);
		ChunkDescriptor key = new ChunkDescriptor(dataset, 0L, chunkIdx);
		_chunkCache.BuildOrUseExisting(key, (int)dsParams.ElementsPerChunk, (Memory<T> buf) =>
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (hr == null)
			{
				_wrapper.UseResource((H5Reader h5Reader) =>
				{
					using H5DatasetID dataset3 = h5Reader.OpenDataset(dataset);
					h5Reader.ReadChunkDirect(dataset3, new int[2] { 0, chunkIdx }, buf);
				});
			}
			else
			{
				using H5DatasetID dataset2 = hr.OpenDataset(dataset);
				hr.ReadChunkDirect(dataset2, new int[2] { 0, chunkIdx }, buf);
			}
			stopwatch.Stop();
			_logger.LogDebug("{Dataset} chunk read in {Duration} ms.", dataset, stopwatch.Elapsed.TotalMilliseconds);
		}, (Memory<T> chunkBuf) =>
		{
			Span<T> outBufferSpan = outBuffer.Span;
			Span<T> chunkBufSpan = chunkBuf.Span;
			for (int i = 0; i < dsParams.Dimensions[0]; i++)
			{
				int chunkDimensionsDsParamsTerm = i * (int)dsParams.ChunkDimensions[1];
				outBufferSpan[i] = chunkBufSpan[chunkDimensionsDsParamsTerm + readCol];
			}
		});
	}

	private void AssertColChunked(DatasetParameters dsParams)
	{
		if (dsParams.Dimensions[0] != dsParams.ChunkDimensions[0])
		{
			string message = dsParams.Name + " is not chunked by columns.";
			_logger.LogError(message);
			throw new Exception(message);
		}
	}

	private static int ChunkIndex(long[] chunkDims, int colIdx)
	{
		return (int)Math.Floor((double)colIdx / (double)chunkDims[1]);
	}

	private static bool IsOOB(long[] dsetDims, int colIdx)
	{
		if (colIdx < dsetDims[1])
		{
			return colIdx < 0;
		}
		return true;
	}

	public void Dispose()
	{
		((IDisposable)_wrapper).Dispose();
	}
}
