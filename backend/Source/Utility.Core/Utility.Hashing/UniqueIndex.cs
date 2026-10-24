using System;
using System.Collections.Generic;

namespace Utility.Hashing;

public class UniqueIndex<TItem, TIndex>
{
	public HashSet<TItem> AllItems;

	public Dictionary<TIndex, TItem> IndexedItems;

	private readonly Func<TItem, TIndex> _getIndexedProperty;

	public UniqueIndex(Func<TItem, TIndex> getProperty)
	{
		_getIndexedProperty = getProperty;
		AllItems = new HashSet<TItem>();
		IndexedItems = new Dictionary<TIndex, TItem>();
	}

	public UniqueIndex(Func<TItem, TIndex> getProperty, IEqualityComparer<TItem> equalityComparer)
	{
		_getIndexedProperty = getProperty;
		AllItems = new HashSet<TItem>(equalityComparer);
		IndexedItems = new Dictionary<TIndex, TItem>();
	}

	public UniqueIndex(Func<TItem, TIndex> getProperty, HashSet<TItem> itemCollection)
	{
		_getIndexedProperty = getProperty;
		AllItems = itemCollection;
		IndexedItems = new Dictionary<TIndex, TItem>();
	}

	public void Add(TItem actualValue2)
	{
		TIndex key = _getIndexedProperty(actualValue2);
		if (!AllItems.TryGetValue(actualValue2, out var actualValue))
		{
			AllItems.Add(actualValue2);
			actualValue = actualValue2;
		}
		IndexedItems[key] = actualValue;
	}

	public TItem GetItemOrDefault(TIndex propValue)
	{
		if (IndexedItems.TryGetValue(propValue, out var localValue))
		{
			return localValue;
		}
		return default;
	}
}
