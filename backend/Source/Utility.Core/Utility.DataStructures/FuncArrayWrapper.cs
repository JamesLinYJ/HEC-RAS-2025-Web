using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Utility.DataStructures;

public class FuncArrayWrapper<T> : IArray<T>, IEnumerable<T>, IEnumerable
{
	private readonly Func<T> _getValue;

	private readonly Func<int> _getCount;

	public T this[int index]
	{
		get
		{
			return _getValue();
		}
		set
		{
		}
	}

	public int Count => _getCount();

	public IList<T> BackingBuffer => null;

	public FuncArrayWrapper(Func<T> getValue, Func<int> getCount)
	{
		_getValue = getValue;
		_getCount = getCount;
	}

	public T First()
	{
		return _getValue();
	}

	public T Last()
	{
		return _getValue();
	}

	public List<T> ToList()
	{
		return Enumerable.Repeat(_getValue(), _getCount()).ToList();
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
		T getValue = _getValue();
		for (int i = 0; i < ct; i++)
		{
			yield return getValue;
		}
	}
}
