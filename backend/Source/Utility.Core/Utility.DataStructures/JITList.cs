using System;
using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public class JITList<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable
{
	private readonly Func<int, T> _getter;

	private readonly bool _shouldCache;

	private readonly bool[] _alreadyAcquired;

	private readonly T[] _cachedItems;

	public T this[int index]
	{
		get
		{
			if (_shouldCache)
			{
				if (!_alreadyAcquired[index])
				{
					T cachedItems = _getter(index);
					_cachedItems[index] = cachedItems;
					_alreadyAcquired[index] = true;
					return cachedItems;
				}
				return _cachedItems[index];
			}
			return _getter(index);
		}
		set
		{
			throw new NotImplementedException();
		}
	}

	public int Count { get; }

	public bool IsReadOnly => true;

	public JITList(int count, bool shouldCache, Func<int, T> getter)
	{
		_getter = getter;
		Count = count;
		_shouldCache = shouldCache;
		if (_shouldCache)
		{
			_alreadyAcquired = new bool[count];
			_cachedItems = new T[count];
		}
	}

	public void Add(T localValue)
	{
		throw new NotImplementedException();
	}

	public void Clear()
	{
		throw new NotImplementedException();
	}

	public bool Contains(T localValue)
	{
		for (int i = 0; i < Count; i++)
		{
			if (localValue.Equals(this[i]))
			{
				return true;
			}
		}
		return false;
	}

	public void CopyTo(T[] values, int arrayIndex)
	{
		for (int i = 0; i < Count; i++)
		{
			values[arrayIndex + i] = this[i];
		}
	}

	public int IndexOf(T localValue)
	{
		for (int i = 0; i < Count; i++)
		{
			if (localValue.Equals(this[i]))
			{
				return i;
			}
		}
		return -1;
	}

	public void Insert(int index, T localValue)
	{
		throw new NotImplementedException();
	}

	public bool Remove(T localValue)
	{
		throw new NotImplementedException();
	}

	public void RemoveAt(int index)
	{
		throw new NotImplementedException();
	}

	public IEnumerator<T> GetEnumerator()
	{
		for (int i = 0; i < Count; i++)
		{
			yield return this[i];
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
