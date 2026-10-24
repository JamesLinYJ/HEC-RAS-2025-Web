using System;
using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public struct ConstReadOnlyList<TModel, TValue> : IReadOnlyList<TValue>, IEnumerable<TValue>, IEnumerable, IReadOnlyCollection<TValue>, IList<TValue>, ICollection<TValue>
{
	private readonly Func<TModel, TValue> _getValue;

	private readonly Func<TModel, int> _getCount;

	private TModel _model;

	public TValue Value => _getValue(_model);

	public TValue this[int index]
	{
		get
		{
			return Value;
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public int Count => _getCount(_model);

	public bool IsReadOnly => true;

	public ConstReadOnlyList(TModel model, Func<TModel, TValue> getValue, Func<TModel, int> getCount)
	{
		_model = model;
		_getCount = getCount;
		_getValue = getValue;
	}

	public IEnumerator<TValue> GetEnumerator()
	{
		int cc = Count;
		TValue v = Value;
		for (int c = 0; c < cc; c++)
		{
			yield return v;
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		int cc = Count;
		TValue v = Value;
		for (int c = 0; c < cc; c++)
		{
			yield return v;
		}
	}

	public int IndexOf(TValue tValue)
	{
		throw new NotSupportedException();
	}

	public void Insert(int index, TValue tValue)
	{
		throw new NotSupportedException();
	}

	public void RemoveAt(int index)
	{
		throw new NotSupportedException();
	}

	public void Add(TValue tValue)
	{
		throw new NotSupportedException();
	}

	public void Clear()
	{
		throw new NotSupportedException();
	}

	public bool Contains(TValue tValue)
	{
		throw new NotSupportedException();
	}

	public void CopyTo(TValue[] tValues, int arrayIndex)
	{
		throw new NotSupportedException();
	}

	public bool Remove(TValue tValue)
	{
		throw new NotSupportedException();
	}
}
public class ConstReadOnlyList<T> : IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, IList<T>, ICollection<T>
{
	private readonly Func<T> _getValue;

	private readonly T _value;

	private readonly Func<int> _getCount;

	private readonly int _count;

	public T Value
	{
		get
		{
			if (_getValue == null)
			{
				return _value;
			}
			return _getValue();
		}
	}

	public T this[int index]
	{
		get
		{
			return Value;
		}
		set
		{
			throw new NotSupportedException();
		}
	}

	public int Count => _getCount?.Invoke() ?? _count;

	public bool IsReadOnly => true;

	public ConstReadOnlyList(T localValue, int count)
	{
		_value = localValue;
		_count = count;
	}

	public ConstReadOnlyList(T localValue, Func<int> getCount)
	{
		_value = localValue;
		_getCount = getCount;
	}

	public ConstReadOnlyList(Func<T> getValue, Func<int> getCount)
	{
		_getValue = getValue;
		_getCount = getCount;
	}

	public IEnumerator<T> GetEnumerator()
	{
		int cc = Count;
		T v = Value;
		for (int c = 0; c < cc; c++)
		{
			yield return v;
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		int cc = Count;
		T v = Value;
		for (int c = 0; c < cc; c++)
		{
			yield return v;
		}
	}

	public int IndexOf(T localValue)
	{
		throw new NotSupportedException();
	}

	public void Insert(int index, T localValue)
	{
		throw new NotSupportedException();
	}

	public void RemoveAt(int index)
	{
		throw new NotSupportedException();
	}

	public void Add(T localValue)
	{
		throw new NotSupportedException();
	}

	public void Clear()
	{
		throw new NotSupportedException();
	}

	public bool Contains(T localValue)
	{
		throw new NotSupportedException();
	}

	public void CopyTo(T[] values, int arrayIndex)
	{
		throw new NotSupportedException();
	}

	public bool Remove(T localValue)
	{
		throw new NotSupportedException();
	}
}
