using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Utility.DataStructures;

public class BindableArrayWrapper<T> : INotifyList<T>, IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged, IList<T>, ICollection<T>, INotifyList, IList, ICollection
{
	private class ReentrancyDisposable : IDisposable
	{
		private readonly BindableArrayWrapper<T> _model;

		public ReentrancyDisposable(BindableArrayWrapper<T> model)
		{
			_model = model;
		}

		public void Dispose()
		{
			_model._blockReentrancyCount--;
		}
	}

	private T[] _values;

	private int _size;

	private readonly Func<T[], T[]> _setValues;

	private int _blockReentrancyCount;

	private T _default;

	public T this[int index]
	{
		get
		{
			if (index < 0 || index >= _size)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			return _values[index];
		}
		set
		{
			if (index < 0)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			CheckReentrancy();
			if (IsReadOnly)
			{
				throw new NotSupportedException("Collection is read-only.");
			}
			if (IsFixedSize && index >= _size)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			if (!IsFixedSize)
			{
				EnsureCapacity(index + 1);
				if (_size <= index)
				{
					_size = index + 1;
					CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
				}
			}
			T replaceIds = _values[index];
			_values[index] = value;
			using (BlockReentrancy())
			{
				CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, replaceIds, index));
			}
		}
	}

	object IList.this[int index]
	{
		get
		{
			return this[index];
		}
		set
		{
			if (!(value is T))
			{
				throw new ArgumentException("Invalid type", "value");
			}
			this[index] = (T)value;
		}
	}

	public int Count => _size;

	public bool IsReadOnly { get; set; }

	public bool IsFixedSize { get; set; }

	public bool IsSynchronized => false;

	public object SyncRoot => this;

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	private IDisposable BlockReentrancy()
	{
		_blockReentrancyCount++;
		return new ReentrancyDisposable(this);
	}

	private void CheckReentrancy()
	{
		if (_blockReentrancyCount > 0 && CollectionChanged != null && CollectionChanged.GetInvocationList().Length > 1)
		{
			throw new InvalidOperationException("Cannot change collection during a collection change event.");
		}
	}

	public BindableArrayWrapper(T[] initialValues, Func<T[], T[]> setValues = null, T defaultVal = default(T), bool isFixedSize = true)
	{
		IsFixedSize = isFixedSize;
		_default = defaultVal;
		_values = initialValues;
		_size = initialValues?.Length ?? 0;
		_setValues = setValues;
	}

	private void EnsureCapacity(int min)
	{
		int ordinal = _values?.Length ?? 0;
		if (ordinal < min)
		{
			int count = ((ordinal == 0) ? 4 : (_values.Length * 2));
			if (count < min)
			{
				count = min;
			}
			T[] values = new T[count];
			Array.Fill(values, _default);
			if (_values != null)
			{
				Array.Copy(_values, 0, values, 0, _size);
			}
			if (_setValues != null)
			{
				values = _setValues(values);
			}
			_values = values;
		}
	}

	public void Add(T addIds)
	{
		CheckReentrancy();
		if (IsReadOnly)
		{
			throw new NotSupportedException("Collection is read-only.");
		}
		int size = _size;
		EnsureCapacity(_size + 1);
		_values[_size] = addIds;
		_size++;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, addIds, size));
		}
	}

	public int Add(object value)
	{
		if (!(value is T))
		{
			throw new ArgumentException("Invalid type", "value");
		}
		Add((T)value);
		return _size - 1;
	}

	public void Insert(int index, T addIds)
	{
		CheckReentrancy();
		if (IsReadOnly)
		{
			throw new NotSupportedException("Collection is read-only.");
		}
		if (index < 0 || index > _size)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		EnsureCapacity(_size + 1);
		if (index < _size)
		{
			Array.Copy(_values, index, _values, index + 1, _size - index);
		}
		_values[index] = addIds;
		_size++;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, addIds, index));
		}
	}

	public void Insert(int index, object value)
	{
		if (!(value is T))
		{
			throw new ArgumentException("Invalid type", "value");
		}
		Insert(index, (T)value);
	}

	public void RemoveAt(int index)
	{
		CheckReentrancy();
		if (IsReadOnly)
		{
			throw new NotSupportedException("Collection is read-only.");
		}
		if (index < 0 || index >= _size)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		T removeIds = _values[index];
		if (index < _size - 1)
		{
			Array.Copy(_values, index + 1, _values, index, _size - index - 1);
		}
		_size--;
		_values[_size] = default;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removeIds, index));
		}
	}

	public bool Remove(T localValue)
	{
		int indexOf = IndexOf(localValue);
		if (indexOf >= 0)
		{
			RemoveAt(indexOf);
			return true;
		}
		return false;
	}

	public void Remove(object localValue2)
	{
		if (localValue2 is T localValue)
		{
			Remove(localValue);
		}
	}

	public void Clear()
	{
		CheckReentrancy();
		if (IsReadOnly)
		{
			throw new NotSupportedException("Collection is read-only.");
		}
		for (int i = 0; i < _size; i++)
		{
			_values[i] = default;
		}
		_size = 0;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}
	}

	public int IndexOf(T localValue)
	{
		EqualityComparer<T> equalityComparer = EqualityComparer<T>.Default;
		for (int i = 0; i < _size; i++)
		{
			if (equalityComparer.Equals(_values[i], localValue))
			{
				return i;
			}
		}
		return -1;
	}

	public int IndexOf(object localValue2)
	{
		if (localValue2 is T localValue)
		{
			return IndexOf(localValue);
		}
		return -1;
	}

	public bool Contains(T localValue)
	{
		return IndexOf(localValue) >= 0;
	}

	public bool Contains(object localValue2)
	{
		if (localValue2 is T localValue)
		{
			return Contains(localValue);
		}
		return false;
	}

	public void CopyTo(T[] values, int arrayIndex)
	{
		Array.Copy(_values, 0, values, arrayIndex, _size);
	}

	public void CopyTo(Array values, int index)
	{
		Array.Copy(_values, 0, values, index, _size);
	}

	public IEnumerator<T> GetEnumerator()
	{
		for (int i = 0; i < _size; i++)
		{
			yield return _values[i];
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public bool AddAll(IEnumerable<T> data, bool fullRefresh = false)
	{
		throw new NotImplementedException();
	}

	public bool RemoveAll(IEnumerable<T> data, bool fullRefresh = false)
	{
		throw new NotImplementedException();
	}
}
