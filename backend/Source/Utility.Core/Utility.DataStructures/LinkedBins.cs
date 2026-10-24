using System;
using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class LinkedBins<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable
{
	public const int DefaultBinSize = 100;

	public readonly int BinSize = 100;

	private readonly List<T[]> _bins;

	private readonly List<int> _startIndexes;

	private readonly List<int> _counts;

	private int _count;

	public int Count => _count;

	public bool IsReadOnly => false;

	public T this[int index]
	{
		get
		{
			int index2 = FindBinIndex(index, out var indexWithinBin);
			return _bins[index2][indexWithinBin];
		}
		set
		{
			throw new NotImplementedException();
		}
	}

	public LinkedBins()
	{
		_startIndexes = new List<int>();
		_counts = new List<int>();
		_bins = new List<T[]>();
		_startIndexes.Add(0);
		_counts.Add(0);
		AddNextBin();
	}

	private void Initialize(IList<T> elements = null)
	{
		throw new NotImplementedException();
	}

	private void AddNextBin()
	{
		T[] elements = new T[BinSize];
		_bins.Add(elements);
		_counts.Add(0);
		_startIndexes.Add(_count);
	}

	private void InsertBinBefore(int binIdx)
	{
		T[] elements = new T[BinSize];
		_bins.Insert(binIdx, elements);
		_counts.Insert(binIdx, 0);
		int startIndex = _startIndexes[binIdx];
		_startIndexes.Insert(binIdx, startIndex);
	}

	public int FindBinIndex(int pointIndex, out int indexWithinBin)
	{
		int binarySearch = _startIndexes.BinarySearch(pointIndex);
		if (binarySearch < 0)
		{
			binarySearch = ~binarySearch - 1;
		}
		indexWithinBin = pointIndex - _startIndexes[binarySearch];
		return binarySearch;
	}

	private void IncrementAllStartIndexesAfter(int binIndex, int amount)
	{
		for (int i = binIndex + 1; i < _bins.Count; i++)
		{
			_startIndexes[i] += amount;
		}
	}

	public void Add(T bins)
	{
		int count = _bins.Count;
		int count2 = _counts[count - 1];
		if (count2 == BinSize)
		{
			AddNextBin();
			_bins[count][0] = bins;
			_counts[count] = 1;
		}
		else
		{
			_bins[count - 1][count2] = bins;
			_counts[count - 1] = _counts[count - 1] + 1;
		}
		_count++;
	}

	public void Clear()
	{
		_bins.Clear();
		_startIndexes.Clear();
		_counts.Clear();
		_count = 0;
		AddNextBin();
	}

	public void CopyTo(T[] values, int arrayIndex)
	{
		throw new NotImplementedException();
	}

	public void Insert(int pointIndex, T localValue)
	{
		if (pointIndex > _count || pointIndex < 0)
		{
			throw new ArgumentOutOfRangeException("pointIndex");
		}
		if (pointIndex == _count)
		{
			Add(localValue);
			return;
		}
		int binIndex = FindBinIndex(pointIndex, out var indexWithinBin);
		if (indexWithinBin == 0)
		{
			InsertBinBefore(binIndex);
			_bins[binIndex][0] = localValue;
			_counts[binIndex] = 1;
			IncrementAllStartIndexesAfter(binIndex, 1);
		}
		else
		{
			int count = _counts[binIndex];
			if (count == BinSize)
			{
				if (binIndex == _bins.Count - 1)
				{
					AddNextBin();
					T[] bin = _bins[binIndex];
					T[] binArray = _bins[binIndex + 1];
					for (int i = indexWithinBin; i < BinSize; i++)
					{
						binArray[i - indexWithinBin] = bin[i];
					}
					bin[indexWithinBin] = localValue;
					_counts[binIndex] = indexWithinBin + 1;
					_counts[binIndex + 1] = BinSize - indexWithinBin;
					_startIndexes[binIndex + 1] = _startIndexes[binIndex + 1] + indexWithinBin;
				}
			}
			else
			{
				T[] bin2 = _bins[binIndex];
				for (int j = indexWithinBin; j < count; j++)
				{
					bin2[j + 1] = bin2[j];
				}
				bin2[indexWithinBin] = localValue;
				_counts[binIndex] += 1;
				IncrementAllStartIndexesAfter(binIndex, 1);
			}
		}
		_count++;
	}

	public void RemoveAt(int index)
	{
	}

	public bool Contains(T localValue)
	{
		throw new NotImplementedException();
	}

	public IEnumerator<T> GetEnumerator()
	{
		throw new NotImplementedException();
	}

	public int IndexOf(T localValue)
	{
		throw new NotImplementedException();
	}

	public bool Remove(T localValue)
	{
		throw new NotImplementedException();
	}

	private IEnumerator IEnumerable_GetEnumerator()
	{
		throw new NotImplementedException();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		throw new NotImplementedException();
	}
}
