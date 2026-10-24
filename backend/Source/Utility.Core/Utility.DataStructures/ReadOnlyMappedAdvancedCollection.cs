using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Utility.DataStructures;

public class ReadOnlyMappedAdvancedCollection<T, TResult> : IReadOnlyAdvancedCollection<TResult>, IReadOnlyNotifyCollection<TResult>, IReadOnlyList<TResult>, IEnumerable<TResult>, IEnumerable, IReadOnlyCollection<TResult>, INotifyCollectionChanged, IList, ICollection
{
	private Func<T, TResult> _translate;

	private IReadOnlyNotifyCollection<T> _baseCollection;

	private Dictionary<T, TResult> _mappedCollectionCache;

	private AdvancedCollection<TResult> _mappedCollection;

	private object _syncRoot = new object();

	private bool _isReversed;

	public int Count => _baseCollection.Count;

	public TResult this[int index] => _mappedCollection[index];

	public TResult this[T t] => _mappedCollectionCache[t];

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

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	public ReadOnlyMappedAdvancedCollection(IReadOnlyNotifyCollection<T> baseCollection, Func<T, TResult> translate, bool isReversed = false)
	{
		_isReversed = isReversed;
		_translate = translate;
		_mappedCollectionCache = new Dictionary<T, TResult>();
		_mappedCollection = new AdvancedCollection<TResult>();
		_baseCollection = baseCollection;
		InitMappedCollection();
		_baseCollection.CollectionChanged += OnBaseCollectionChanged;
	}

	private void InitMappedCollection()
	{
		_mappedCollection.CollectionChanged -= OnMappedCollectionChanged;
		if (_isReversed)
		{
			for (int i = 0; i < _baseCollection.Count; i++)
			{
				IReadOnlyNotifyCollection<T> baseCollection = _baseCollection;
				int ordinal = i + 1;
				T baseCollection2 = baseCollection[baseCollection.Count - ordinal];
				AddToMapped(baseCollection2);
			}
		}
		else
		{
			foreach (T baseCollection3 in _baseCollection)
			{
				AddToMapped(baseCollection3);
			}
		}
		_mappedCollection.CollectionChanged += OnMappedCollectionChanged;
	}

	private void InsertToMapped(int index, T localValue)
	{
		TResult mappedCollectionCache = _translate(localValue);
		_mappedCollectionCache[localValue] = mappedCollectionCache;
		_mappedCollection.Insert(index, mappedCollectionCache);
	}

	private void AddToMapped(T localValue)
	{
		TResult mappedCollectionCache = _translate(localValue);
		_mappedCollectionCache[localValue] = mappedCollectionCache;
		_mappedCollection.Add(mappedCollectionCache);
	}

	private void AddAllToMapped(IEnumerable<T> items)
	{
		List<TResult> tResults = new List<TResult>();
		foreach (T localValue in items)
		{
			TResult registerToMapped = RegisterToMapped(localValue);
			tResults.Add(registerToMapped);
		}
		_mappedCollection.AddAll(tResults);
	}

	private void ClearMapped()
	{
		_mappedCollection.Clear();
		_mappedCollectionCache.Clear();
	}

	private TResult RegisterToMapped(T localValue)
	{
		TResult mappedCollectionCache = _translate(localValue);
		_mappedCollectionCache[localValue] = mappedCollectionCache;
		return mappedCollectionCache;
	}

	private void OnMappedCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		CollectionChanged?.Invoke(this, e);
	}

	private void OnBaseCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			ClearMapped();
			InitMappedCollection();
			return;
		}
		if (e.OldItems != null)
		{
			List<TResult> tResults = new List<TResult>();
			foreach (T cast in e.OldItems.Cast<T>())
			{
				if (_mappedCollectionCache.ContainsKey(cast))
				{
					tResults.Add(_mappedCollectionCache[cast]);
				}
			}
			_mappedCollection.RemoveAll(tResults);
		}
		if (e.NewItems == null)
		{
			return;
		}
		IEnumerable<T> enumerable = e.NewItems.Cast<T>();
		if (_isReversed)
		{
			int newStartingIndex = e.NewStartingIndex;
			int index = Count - newStartingIndex - 1;
			{
				foreach (T enumerable2 in enumerable)
				{
					InsertToMapped(index, enumerable2);
				}
				return;
			}
		}
		int newStartingIndex2 = e.NewStartingIndex;
		foreach (T enumerable3 in enumerable)
		{
			InsertToMapped(newStartingIndex2, enumerable3);
			newStartingIndex2++;
		}
	}

	public bool Contains(TResult element)
	{
		return _mappedCollection.Contains(element);
	}

	public IEnumerator<TResult> GetEnumerator()
	{
		return _mappedCollection.GetEnumerator();
	}

	public int IndexOf(TResult element)
	{
		return _mappedCollection.IndexOf(element);
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public bool Contains(object localValue)
	{
		if (!(localValue is TResult tResult))
		{
			return false;
		}
		return _mappedCollection.Contains(tResult);
	}

	public int IndexOf(object localValue)
	{
		if (!(localValue is TResult tResult))
		{
			return -1;
		}
		return _mappedCollection.IndexOf(tResult);
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
