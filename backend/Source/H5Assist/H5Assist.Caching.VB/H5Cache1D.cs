using System;
using System.Threading;
using H5Assist.Chunking.VB;
using H5Assist.VB;

namespace H5Assist.Caching.VB;

public class H5Cache1D<T> : H5Cache<T, Chunk1D<T>> where T : struct
{
	public H5Cache1D(H5Assist.VB.H5Reader h5r)
		: base(h5r)
	{
	}

	public H5Cache1D(string filename)
		: base(filename)
	{
	}

	protected override int GetNumDimensionsExpected()
	{
		return 1;
	}

	protected override int ComputeBytesPerChunk(long[] chunkDims)
	{
		return (int)chunkDims[0] * SizeOfT;
	}

	public int GetLineIndex(int dsetIndex, int itemIndex)
	{
		long[] chunkDimensions = _datasetParameters[dsetIndex].ChunkDimensions;
		return ChunkIndex(chunkDimensions, itemIndex);
	}

	private int ChunkIndex(long[] chunkDims, int itemIdx)
	{
		return (int)Math.Floor((double)itemIdx / (double)chunkDims[0]);
	}

	private bool IsOOB(long[] dsetDims, int itemIdx)
	{
		if (itemIdx >= dsetDims[0])
		{
			return true;
		}
		return false;
	}

	public T Item(int dsetIndex, int itemIndex)
	{
		long[] chunkDimensions = _datasetParameters[dsetIndex].ChunkDimensions;
		long[] dimensions = _datasetParameters[dsetIndex].Dimensions;
		if (IsOOB(dimensions, itemIndex))
		{
			throw new IndexOutOfRangeException("start index");
		}
		int dimensionsTerm = Convert.ToInt32(dimensions[0]);
		int chunkIndex = ChunkIndex(chunkDimensions, itemIndex);
		int chunkDimensionsTerm = Convert.ToInt32(chunkDimensions[0]);
		lock (CacheChunkLocks[dsetIndex][chunkIndex])
		{
			WaitUntilEvictionInactive(dsetIndex);
			Chunk1D<T> chunk1D2 = null;
			if (Cache[dsetIndex].TryGetValue(chunkIndex, out chunk1D2))
			{
				return chunk1D2.GetItem(itemIndex);
			}
			string name = _datasetParameters[dsetIndex].Name;
			int chunkIndexTerm = chunkIndex * chunkDimensionsTerm;
			int count = Convert.ToInt32(Math.Min(dimensionsTerm - chunkIndexTerm, chunkDimensionsTerm));
			T[] values = new T[count];
			if (_openFileOnDemand)
			{
				using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(_filename);
				h5Reader.ReadElements(name, chunkIndexTerm, count, ref values);
			}
			else
			{
				_h5r.ReadElements(name, chunkIndexTerm, count, ref values);
			}
			Chunk1D<T> chunk1D = new Chunk1D<T>(values, chunkIndexTerm);
			Cache[dsetIndex].TryAdd(chunkIndex, chunk1D);
			lock (Cache[dsetIndex])
			{
				_datasetParameters[dsetIndex].ActiveThreadCount--;
			}
			CleanIfNeeded(dsetIndex);
			return chunk1D.GetItem(itemIndex);
		}
	}

	private void WaitUntilEvictionInactive(int datasetIndex)
	{
		while (_datasetParameters[datasetIndex].EvictionActive)
		{
			Thread.Sleep(1);
		}
		lock (Cache[datasetIndex])
		{
			_datasetParameters[datasetIndex].ActiveThreadCount++;
		}
	}

	public T[] GetLineContaining(int dsetIndex, int chunkIdx, ref int startItemIdx)
	{
		long[] chunkDimensions = _datasetParameters[dsetIndex].ChunkDimensions;
		int dimensionsDatasetParametersTerm = Convert.ToInt32(_datasetParameters[dsetIndex].Dimensions[0]);
		int chunkDimensionsTerm = Convert.ToInt32(chunkDimensions[0]);
		startItemIdx = chunkIdx * chunkDimensionsTerm;
		lock (CacheChunkLocks[dsetIndex][chunkIdx])
		{
			Chunk1D<T> chunk1D = null;
			if (Cache[dsetIndex].TryGetValue(chunkIdx, out chunk1D))
			{
				return chunk1D.GetLine();
			}
			string name = _datasetParameters[dsetIndex].Name;
			int count = Convert.ToInt32(Math.Min(dimensionsDatasetParametersTerm - startItemIdx, chunkDimensionsTerm));
			T[] values = new T[count];
			if (_openFileOnDemand)
			{
				using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(_filename);
				h5Reader.ReadElements(name, startItemIdx, count, ref values);
			}
			else
			{
				_h5r.ReadElements(name, startItemIdx, count, ref values);
			}
			Chunk1D<T> startItemIdxTerm = new Chunk1D<T>(values, startItemIdx);
			Cache[dsetIndex].TryAdd(chunkIdx, startItemIdxTerm);
			CleanIfNeeded(dsetIndex);
			return values;
		}
	}

	public override void EnsureDataset(string dset, bool pinned = false)
	{
		lock (Cache)
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
	}
}
