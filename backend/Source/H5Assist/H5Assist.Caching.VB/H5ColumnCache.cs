using System;
using H5Assist.Chunking.VB;
using H5Assist.VB;

namespace H5Assist.Caching.VB;

public class H5ColumnCache<T> : H5Cache2D<T> where T : struct
{
	public H5ColumnCache(H5Assist.VB.H5Reader h5r)
		: base(h5r)
	{
	}

	public H5ColumnCache(string filename)
		: base(filename)
	{
	}

	public override void EnsureDataset(string dset, bool pinned = false)
	{
		lock (Cache)
		{
			if (Datasets.Contains(dset))
			{
				return;
			}
			if (_openFileOnDemand)
			{
				using (H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(_filename))
				{
					AddFileInternal(hr, dset, chunkedByRows: false, pinned);
					return;
				}
			}
			AddFileInternal(_h5r, dset, chunkedByRows: false, pinned);
		}
	}

	public T[] EnsureGetColumn(string dataset, int readCol)
	{
		EnsureDataset(dataset);
		return Item(GetIndex(dataset), readCol);
	}

	public override T[] Item(int dsetIndex, int columnIndex)
	{
		long[] chunkDimensions = _datasetParameters[dsetIndex].ChunkDimensions;
		long[] dimensions = _datasetParameters[dsetIndex].Dimensions;
		if (IsOOB(dimensions, columnIndex))
		{
			throw new IndexOutOfRangeException("columnIndex");
		}
		int dimension = (int)dimensions[0];
		int dimension2 = (int)dimensions[1];
		int chunkDimension = (int)chunkDimensions[1];
		int chunkIndex = ChunkIndex(chunkDimensions, columnIndex);
		lock (CacheChunkLocks[dsetIndex][chunkIndex])
		{
			if (Cache[dsetIndex].TryGetValue(chunkIndex, out var localValue))
			{
				return localValue.GetLine(columnIndex);
			}
			string name = _datasetParameters[dsetIndex].Name;
			int chunkIndexTimesChunkDimension = chunkIndex * chunkDimension;
			int count = Convert.ToInt32(Math.Min(dimension2 - chunkIndexTimesChunkDimension, chunkDimension));
			T[,] values = new T[dimension, count];
			if (_openFileOnDemand)
			{
				using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(_filename);
				h5Reader.ReadColumns(name, chunkIndexTimesChunkDimension, count, ref values);
			}
			else
			{
				_h5r.ReadColumns(name, chunkIndexTimesChunkDimension, count, ref values);
			}
			Chunk2D<T> chunk2D = Chunk2D<T>.ParseColumns(values, chunkIndexTimesChunkDimension);
			Cache[dsetIndex].TryAdd(chunkIndex, chunk2D);
			CleanIfNeeded(dsetIndex);
			return chunk2D.GetLine(columnIndex);
		}
	}

	private int ChunkIndex(long[] chunkDims, int colIdx)
	{
		return (int)Math.Floor((double)colIdx / (double)chunkDims[1]);
	}

	private bool IsOOB(long[] dsetDims, int colIdx)
	{
		if (colIdx < dsetDims[1])
		{
			return colIdx < 0;
		}
		return true;
	}
}
