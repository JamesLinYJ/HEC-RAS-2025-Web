using System;
using System.Diagnostics;
using H5Assist.Chunking.VB;
using H5Assist.VB;
using Microsoft.Extensions.Logging;

namespace H5Assist.Caching.VB;

public class H5RowCache<T> : H5Cache2D<T> where T : struct
{
	public H5RowCache(H5Assist.VB.H5Reader h5r)
		: base(h5r)
	{
	}

	public H5RowCache(string filename)
		: base(filename)
	{
	}

	public void PinDataset(string dataset)
	{
		EnsureDataset(dataset, pinned: true);
		int index = GetIndex(dataset);
		DatasetParameters datasetParameters = _datasetParameters[index];
		for (int i = 0; i < datasetParameters.Dimensions[0]; i++)
		{
			Item(index, i);
		}
	}

	public T[] EnsureGetRow(string dataset, int readRow)
	{
		EnsureDataset(dataset);
		return Item(GetIndex(dataset), readRow);
	}

	public override T[] Item(int dsetIndex, int rowIndex)
	{
		long[] chunkDimensions = _datasetParameters[dsetIndex].ChunkDimensions;
		long[] dimensions = _datasetParameters[dsetIndex].Dimensions;
		if (IsOOB(dimensions, rowIndex))
		{
			throw new IndexOutOfRangeException("rowIndex");
		}
		int dimension = (int)dimensions[1];
		int dimension2 = (int)dimensions[0];
		int chunkDimension = (int)chunkDimensions[0];
		int chunkIndex = ChunkIndex(chunkDimensions, rowIndex);
		lock (CacheChunkLocks[dsetIndex][chunkIndex])
		{
			if (Cache[dsetIndex].TryGetValue(chunkIndex, out var localValue))
			{
				return localValue.GetLine(rowIndex);
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			string name = _datasetParameters[dsetIndex].Name;
			int chunkIndexTimesChunkDimension = chunkIndex * chunkDimension;
			int count = Convert.ToInt32(Math.Min(dimension2 - chunkIndexTimesChunkDimension, chunkDimension));
			T[,] values = new T[count, dimension];
			if (_openFileOnDemand)
			{
				using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(_filename);
				h5Reader.ReadRows(name, chunkIndexTimesChunkDimension, count, ref values);
			}
			else
			{
				_h5r.ReadRows(name, chunkIndexTimesChunkDimension, count, ref values);
			}
			Chunk2D<T> chunk2D = Chunk2D<T>.ParseRows(values, chunkIndexTimesChunkDimension);
			Cache[dsetIndex].TryAdd(chunkIndex, chunk2D);
			CleanIfNeeded(dsetIndex);
			T[] line = chunk2D.GetLine(rowIndex);
			stopwatch.Stop();
			_logger.LogDebug("{Dataset}:{Chunk} read into cache: {Duration} ms.", _datasetParameters[dsetIndex].Name, chunkIndex, stopwatch.Elapsed.TotalMilliseconds);
			return line;
		}
	}

	private int ChunkIndex(long[] chunkDims, int rowIdx)
	{
		return (int)Math.Floor((double)rowIdx / (double)chunkDims[0]);
	}

	private bool IsOOB(long[] dsetDims, int rowIdx)
	{
		if (rowIdx < dsetDims[0])
		{
			return rowIdx < 0;
		}
		return true;
	}

	public override void EnsureDataset(string dset, bool pinned = false)
	{
		lock (Cache)
		{
			if (!Datasets.Contains(dset))
			{
				if (_openFileOnDemand)
				{
					using (H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(_filename))
					{
						AddFileInternal(hr, dset, chunkedByRows: true, pinned);
						return;
					}
				}
				AddFileInternal(_h5r, dset, chunkedByRows: true, pinned);
			}
			else if (pinned)
			{
				int index = GetIndex(dset);
				_datasetParameters[index].Pinned = true;
			}
		}
	}
}
