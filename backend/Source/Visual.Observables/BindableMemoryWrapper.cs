using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.DataStructures;

public class BindableMemoryWrapper<T> : ObservableObject, INotifyList<T>, IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged, IList<T>, ICollection<T>, INotifyList, IList, ICollection
{
	public struct SetValueArgs
	{
		public BindableMemoryWrapper<T> Sender;

		public Memory<T> OldValues;

		public Memory<T> NewValues;
	}

	private class ReentrancyDisposable : IDisposable
	{
		private readonly BindableMemoryWrapper<T> _model;

		public ReentrancyDisposable(BindableMemoryWrapper<T> model)
		{
			_model = model;
		}

		public void Dispose()
		{
			_model._blockReentrancyCount--;
		}
	}

	private Memory<T> _values;

	private int _size;

	private readonly Func<SetValueArgs, Memory<T>> _setValues;

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
			return _values.Span[index];
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
			if (!IsFixedSize && _size <= index)
			{
				EnsureCapacity(index + 1);
				_size = index + 1;
				CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
				OnPropertyChanged("Count");
			}
			Span<T> buffer = _values.Span;
			T replaceIds = buffer[index];
			buffer[index] = value;
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

	public BindableMemoryWrapper(Memory<T> initialValues, Func<SetValueArgs, Memory<T>> setValues = null, T defaultVal = default(T), bool isFixedSize = true)
	{
		IsFixedSize = isFixedSize;
		_default = defaultVal;
		_values = initialValues;
		_size = initialValues.Length;
		_setValues = setValues;
	}

	private void EnsureCapacity(int min)
	{
		int length = _values.Length;
		if (length < min)
		{
			int count = ((length == 0) ? 4 : (_values.Length * 2));
			if (count < min)
			{
				count = min;
			}
			T[] values = new T[count];
			Array.Fill(values, _default);
			if (!_values.IsEmpty)
			{
				_values.CopyTo(values.AsMemory(0, _size));
			}
			if (_setValues != null)
			{
				_values = _setValues(new SetValueArgs
				{
					Sender = this,
					OldValues = _values,
					NewValues = values.AsMemory().Slice(0, min)
				});
			}
			else
			{
				_values = values;
			}
		}
		else if (_setValues != null)
		{
			_values = _setValues(new SetValueArgs
			{
				Sender = this,
				OldValues = _values,
				NewValues = _values.Slice(0, min)
			});
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
		_values.Span[_size] = addIds;
		_size++;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, addIds, size));
			OnPropertyChanged("Count");
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
			_values.Slice(index).CopyTo(_values.Slice(index + 1));
		}
		_values.Span[index] = addIds;
		_size++;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, addIds, index));
			OnPropertyChanged("Count");
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
		T removeIds = _values.Span[index];
		if (index < _size - 1)
		{
			_values.Slice(index + 1).CopyTo(_values.Slice(index));
		}
		_size--;
		_values.Span[_size] = default;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removeIds, index));
			OnPropertyChanged("Count");
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
		Span<T> buffer = _values.Span;
		for (int i = 0; i < _size; i++)
		{
			buffer[i] = default;
		}
		_size = 0;
		using (BlockReentrancy())
		{
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
			OnPropertyChanged("Count");
		}
	}

	public int IndexOf(T localValue)
	{
		EqualityComparer<T> equalityComparer = EqualityComparer<T>.Default;
		Span<T> buffer = _values.Span;
		for (int i = 0; i < _size; i++)
		{
			if (equalityComparer.Equals(buffer[i], localValue))
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
		_values.Slice(0, _size).CopyTo(values.AsMemory(arrayIndex));
	}

	public void CopyTo(Array values, int index)
	{
		throw new NotSupportedException();
	}

	public IEnumerator<T> GetEnumerator()
	{
		for (int i = 0; i < _size; i++)
		{
			yield return _values.Span[i];
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
