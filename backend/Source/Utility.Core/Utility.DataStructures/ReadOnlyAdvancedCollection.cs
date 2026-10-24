using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Utility.DataStructures;

public class ReadOnlyAdvancedCollection<T> : IReadOnlyAdvancedCollection<T>, IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged, IList<T>, ICollection<T>
{
	private IAdvancedCollection<T> _collection;

	T IList<T>.this[int index]
	{
		get
		{
			return ((IList<T>)_collection)[index];
		}
		set
		{
			((IList<T>)_collection)[index] = value;
		}
	}

	public bool IsReadOnly => true;

	public T this[int index] => ((IList<T>)_collection)[index];

	public int Count => ((ICollection<T>)_collection).Count;

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	public ReadOnlyAdvancedCollection(IAdvancedCollection<T> collection)
	{
		_collection = collection;
		_collection.CollectionChanged += EleCollectionChanged;
	}

	~ReadOnlyAdvancedCollection()
	{
		_collection.CollectionChanged -= EleCollectionChanged;
		_collection = null;
	}

	private void EleCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		CollectionChanged?.Invoke(sender, e);
	}

	public bool Contains(T element)
	{
		return ((ICollection<T>)_collection).Contains(element);
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _collection.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _collection.GetEnumerator();
	}

	public int IndexOf(T t)
	{
		return ((IList<T>)_collection).IndexOf(t);
	}

	public void CopyTo(T[] collection, int arrayIndex)
	{
		_collection.CopyTo(collection, arrayIndex);
	}

	public void Insert(int index, T localValue)
	{
		throw new NotSupportedException("This collection is read only.");
	}

	public void RemoveAt(int index)
	{
		throw new NotSupportedException("This collection is read only.");
	}

	public void Add(T localValue)
	{
		throw new NotSupportedException("This collection is read only.");
	}

	public void Clear()
	{
		throw new NotSupportedException("This collection is read only.");
	}

	public bool Remove(T localValue)
	{
		throw new NotSupportedException("This collection is read only.");
	}
}
