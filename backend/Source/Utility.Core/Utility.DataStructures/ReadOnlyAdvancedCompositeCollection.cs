using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Utility.Helpers;

namespace Utility.DataStructures;

public class ReadOnlyAdvancedCompositeCollection<T> : IReadOnlyAdvancedCollection<T>, IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged, IList, ICollection
{
	private WeakHandler _itemsSource1CollectionChangedHandle;

	private WeakHandler _itemsSource2CollectionChangedHandle;

	private object _syncRoot = new object();

	public IReadOnlyCollection<T> ItemsSource1
	{
		get
		{
			return field;
		}
		set
		{
			if (field == value)
			{
				return;
			}
			_itemsSource1CollectionChangedHandle?.Unsubscribe();
			field = value;
			if (value is INotifyCollectionChanged)
			{
				_itemsSource1CollectionChangedHandle = WeakHandler.TrySubscribeCollectionChanged(field, this, (ReadOnlyAdvancedCompositeCollection<T> model, object sender, NotifyCollectionChangedEventArgs e) =>
				{
					model.OnSource1CollectionChanged(sender, e);
				});
			}
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}
	}

	public IReadOnlyCollection<T> ItemsSource2
	{
		get
		{
			return field;
		}
		set
		{
			if (field == value)
			{
				return;
			}
			_itemsSource2CollectionChangedHandle?.Unsubscribe();
			field = value;
			if (value is INotifyCollectionChanged)
			{
				_itemsSource2CollectionChangedHandle = WeakHandler.TrySubscribeCollectionChanged(field, this, (ReadOnlyAdvancedCompositeCollection<T> model, object sender, NotifyCollectionChangedEventArgs e) =>
				{
					model.OnSource2CollectionChanged(sender, e);
				});
			}
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}
	}

	private int ItemsSource1Count => ItemsSource1?.Count ?? 0;

	private int ItemsSource2Count => ItemsSource2?.Count ?? 0;

	public int Count => ItemsSource1Count + ItemsSource2Count;

	public bool IsFixedSize => false;

	public bool IsReadOnly => true;

	public bool IsSynchronized => false;

	public object SyncRoot => _syncRoot;

	object IList.this[int index]
	{
		get
		{
			return this[index];
		}
		set
		{
			throw new NotSupportedException("This is read only.");
		}
	}

	public T this[int index]
	{
		get
		{
			if (index < 0 || index >= Count)
			{
				throw new IndexOutOfRangeException();
			}
			if (index < ItemsSource1Count)
			{
				return ItemsSource1.ElementAt(index);
			}
			return ItemsSource2.ElementAt(index - ItemsSource1Count);
		}
	}

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	public bool Contains(T element)
	{
		IReadOnlyCollection<T> itemsSource = ItemsSource1;
		if (itemsSource != null && itemsSource.Contains(element))
		{
			return true;
		}
		IReadOnlyCollection<T> itemsSource2 = ItemsSource2;
		if (itemsSource2 != null && itemsSource2.Contains(element))
		{
			return true;
		}
		return false;
	}

	public int IndexOf(T element)
	{
		int unity = -1;
		if (ItemsSource1 != null)
		{
			unity = IndexOfInSource(ItemsSource1, element);
			if (unity != -1)
			{
				return unity;
			}
		}
		if (ItemsSource2 != null)
		{
			unity = IndexOfInSource(ItemsSource2, element);
			if (unity != -1)
			{
				return unity + ItemsSource1Count;
			}
		}
		return -1;
	}

	private int IndexOfInSource(IEnumerable<T> source, T element)
	{
		int i = 0;
		using (IEnumerator<T> source2 = source.GetEnumerator())
		{
			for (; source2.MoveNext(); i++)
			{
				T current = source2.Current;
				// Preserve null/Equals semantics without the decompiler's ref
				// reassignment to a local with a narrower escape scope.
				if (current is null ? element is null : current.Equals(element))
				{
					return i;
				}
			}
		}
		return -1;
	}

	public IEnumerator<T> GetEnumerator()
	{
		if (ItemsSource1 != null)
		{
			foreach (T itemsSource1 in ItemsSource1)
			{
				yield return itemsSource1;
			}
		}
		if (ItemsSource2 == null)
		{
			yield break;
		}
		foreach (T itemsSource2 in ItemsSource2)
		{
			yield return itemsSource2;
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
	{
		CollectionChanged?.Invoke(this, e);
	}

	private void OnSource1CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		OnCollectionChanged(e);
	}

	private void OnSource2CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		int itemsSource1Count = ItemsSource1Count;
		NotifyCollectionChangedEventArgs e2 = null;
		switch (e.Action)
		{
		case NotifyCollectionChangedAction.Add:
			e2 = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, e.NewItems, e.NewStartingIndex + itemsSource1Count);
			break;
		case NotifyCollectionChangedAction.Remove:
			e2 = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, e.OldItems, e.OldStartingIndex + itemsSource1Count);
			break;
		case NotifyCollectionChangedAction.Replace:
			e2 = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, e.NewItems, e.OldItems, e.OldStartingIndex + itemsSource1Count);
			break;
		case NotifyCollectionChangedAction.Move:
			e2 = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, e.NewItems, e.NewStartingIndex + itemsSource1Count, e.OldStartingIndex + itemsSource1Count);
			break;
		case NotifyCollectionChangedAction.Reset:
			e2 = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
			break;
		}
		if (e2 != null)
		{
			OnCollectionChanged(e2);
		}
	}

	public bool Contains(object localValue)
	{
		if (localValue is T element)
		{
			return Contains(element);
		}
		return false;
	}

	public int IndexOf(object localValue)
	{
		if (localValue is T element)
		{
			return IndexOf(element);
		}
		return -1;
	}

	public void CopyTo(Array values, int index)
	{
		throw new NotImplementedException("TODO: Implement");
	}

	public int Add(object localValue)
	{
		throw new NotSupportedException("This is read only.");
	}

	public void Clear()
	{
		throw new NotSupportedException("This is read only.");
	}

	public void Insert(int index, object localValue)
	{
		throw new NotSupportedException("This is read only.");
	}

	public void Remove(object localValue)
	{
		throw new NotSupportedException("This is read only.");
	}

	public void RemoveAt(int index)
	{
		throw new NotSupportedException("This is read only.");
	}
}
