using System;
using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public struct CompactList<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable, IReadOnlyList<T>, IReadOnlyCollection<T>
{
	private T _singleValue;

	private List<T> _multipleValues;

	private int _count;

	public static CompactList<T> Empty => default;

	public int Count => _count;

	public readonly T FirstValue => _singleValue;

	readonly bool ICollection<T>.IsReadOnly => false;

	public T this[int index]
	{
		get
		{
			if (index >= _count || index < 0)
			{
				throw new IndexOutOfRangeException();
			}
			if (index == 0)
			{
				return _singleValue;
			}
			return _multipleValues[index - 1];
		}
		set
		{
			if (index >= _count || index < 0)
			{
				throw new IndexOutOfRangeException();
			}
			if (index == 0)
			{
				_singleValue = value;
			}
			else
			{
				_multipleValues[index - 1] = value;
			}
		}
	}

	public CompactList(T initialValue)
	{
		_multipleValues = null;
		_count = 0;
		_singleValue = initialValue;
		_count++;
	}

	public CompactList(IList<T> initialValues)
	{
		_singleValue = default;
		_multipleValues = null;
		_count = 0;
		if (initialValues != null && initialValues.Count != 0)
		{
			if (initialValues.Count == 1)
			{
				_singleValue = initialValues[0];
				_count = 1;
			}
			else
			{
				AddRange(initialValues);
			}
		}
	}

	public int IndexOf(T localValue)
	{
		if (Count == 0)
		{
			return -1;
		}
		if (_singleValue.Equals(localValue))
		{
			return 0;
		}
		if (Count == 1)
		{
			return -1;
		}
		int indexOf = _multipleValues.IndexOf(localValue);
		if (indexOf == -1)
		{
			return -1;
		}
		return indexOf + 1;
	}

	public void Add(T singleValue)
	{
		if (_count == 0)
		{
			_singleValue = singleValue;
		}
		else
		{
			if (_multipleValues == null)
			{
				_multipleValues = new List<T>();
			}
			_multipleValues.Add(singleValue);
		}
		_count++;
	}

	public void AddRange(IEnumerable<T> values)
	{
		foreach (T localValue in values)
		{
			Add(localValue);
		}
	}

	public void Insert(int idx, T singleValue)
	{
		if (idx > Count || idx < 0)
		{
			throw new IndexOutOfRangeException();
		}
		if (idx == Count)
		{
			Add(singleValue);
		}
		else if (idx == 0)
		{
			if (_multipleValues == null)
			{
				_multipleValues = new List<T>();
			}
			_multipleValues.Insert(0, _singleValue);
			_singleValue = singleValue;
			_count++;
		}
		else
		{
			if (_multipleValues == null)
			{
				_multipleValues = new List<T>();
			}
			_multipleValues.Insert(idx - 1, singleValue);
			_count++;
		}
	}

	public bool Remove(T localValue)
	{
		int indexOf = IndexOf(localValue);
		if (indexOf == -1)
		{
			return false;
		}
		RemoveAt(indexOf);
		return true;
	}

	public void RemoveAt(int idx)
	{
		if (idx < 0 || idx >= Count)
		{
			throw new IndexOutOfRangeException();
		}
		if (idx == 0 && _count > 1)
		{
			_singleValue = _multipleValues[0];
			if (_count != 2)
			{
				_multipleValues.RemoveAt(0);
			}
		}
		else if (idx != 0)
		{
			_multipleValues.RemoveAt(idx - 1);
		}
		_count--;
		if (_count < 2 && _multipleValues != null)
		{
			_multipleValues = null;
		}
	}

	public bool Contains(T localValue)
	{
		return IndexOf(localValue) != -1;
	}

	public void Clear()
	{
		_multipleValues?.Clear();
		_count = 0;
	}

	public void CopyTo(T[] multipleValues, int arrayIndex)
	{
		if (Count != 0)
		{
			if (multipleValues == null)
			{
				throw new ArgumentNullException("Array is null.");
			}
			if (arrayIndex < 0)
			{
				throw new ArgumentOutOfRangeException();
			}
			if (multipleValues.Length - arrayIndex < Count)
			{
				throw new ArgumentException("Array is not long enough to have it copied to.");
			}
			multipleValues[arrayIndex] = _singleValue;
			if (Count != 1)
			{
				_multipleValues.CopyTo(multipleValues, arrayIndex + 1);
			}
		}
	}

	public static implicit operator CompactList<T>(T t)
	{
		return new CompactList<T>(t);
	}

	public IEnumerator<T> GetEnumerator()
	{
		return new CompactListEnumerator<T>(this);
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return new CompactListEnumerator<T>(this);
	}
}
