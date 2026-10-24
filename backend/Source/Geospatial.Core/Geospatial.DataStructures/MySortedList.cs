using System;
using System.Collections.Generic;

namespace Geospatial.DataStructures;

public class MySortedList<TKey> where TKey : IComparable<TKey>
{
	private List<KeyValuePair<TKey, int>> _sortedList;

	public int ValueSum;

	public KeyValuePair<TKey, int> this[int index]
	{
		get
		{
			return _sortedList[index];
		}
		set
		{
			int sortedListIndexTerm = value.Value - _sortedList[index].Value;
			ValueSum += sortedListIndexTerm;
			_sortedList[index] = value;
		}
	}

	public int Count => _sortedList.Count;

	public MySortedList()
	{
		_sortedList = new List<KeyValuePair<TKey, int>>();
	}

	public MySortedList(int capacity)
	{
		_sortedList = new List<KeyValuePair<TKey, int>>(capacity);
	}

	public void Add(KeyValuePair<TKey, int> localValue)
	{
		int binarySearch = BinarySearch(localValue.Key);
		if (binarySearch >= 0 && _sortedList[binarySearch].Key.Equals(localValue.Key))
		{
			throw new ArgumentException("Key already exists");
		}
		if (binarySearch < 0)
		{
			_sortedList.Insert(~binarySearch, localValue);
			ValueSum += localValue.Value;
		}
		else
		{
			_sortedList.Insert(binarySearch, localValue);
			ValueSum += localValue.Value;
		}
	}

	public void Clear()
	{
		ValueSum = 0;
		_sortedList.Clear();
	}

	public bool ContainsKey(TKey tKey)
	{
		if (_sortedList.Count == 0)
		{
			return false;
		}
		int binarySearch = BinarySearch(tKey);
		if (binarySearch >= 0)
		{
			return _sortedList[binarySearch].Key.Equals(tKey);
		}
		return false;
	}

	public int IndexOfKey(TKey tKey)
	{
		return BinarySearch(tKey);
	}

	private int BinarySearch(int index, int length, TKey tKey, IComparer<TKey> comparer)
	{
		if (_sortedList == null)
		{
			throw new ArgumentNullException("_sortedList");
		}
		if (index < 0 || length < 0)
		{
			throw new ArgumentOutOfRangeException((index < 0) ? "index" : "length");
		}
		if (_sortedList.Count - index < length)
		{
			throw new ArgumentException();
		}
		int index2 = index;
		int ordinal = index + length - 1;
		while (index2 <= ordinal)
		{
			int index2Term = index2 + (ordinal - index2 >> 1);
			int compare = comparer.Compare(_sortedList[index2Term].Key, tKey);
			if (compare == 0)
			{
				return index2Term;
			}
			if (compare < 0)
			{
				index2 = index2Term + 1;
			}
			else
			{
				ordinal = index2Term - 1;
			}
		}
		return ~index2;
	}

	private int BinarySearch(TKey tKey)
	{
		return BinarySearch(0, _sortedList.Count, tKey, Comparer<TKey>.Default);
	}
}
