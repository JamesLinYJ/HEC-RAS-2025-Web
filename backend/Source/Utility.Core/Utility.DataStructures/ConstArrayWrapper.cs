using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Utility.DataStructures;

public class ConstArrayWrapper<T> : IArray<T>, IEnumerable<T>, IEnumerable
{
	private readonly T _value;

	private readonly Func<int> _getCount;

	public T this[int index]
	{
		get
		{
			return _value;
		}
		set
		{
		}
	}

	public int Count => _getCount();

	public IList<T> BackingBuffer => null;

	public ConstArrayWrapper(T localValue, Func<int> getCount)
	{
		_value = localValue;
		_getCount = getCount;
	}

	public T First()
	{
		return _value;
	}

	public T Last()
	{
		return _value;
	}

	public List<T> ToList()
	{
		return Enumerable.Repeat(_value, _getCount()).ToList();
	}

	public IEnumerator<T> GetEnumerator()
	{
		return GetEnumeratorInternal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumeratorInternal();
	}

	private IEnumerator<T> GetEnumeratorInternal()
	{
		int ct = _getCount();
		for (int i = 0; i < ct; i++)
		{
			yield return _value;
		}
	}
}
