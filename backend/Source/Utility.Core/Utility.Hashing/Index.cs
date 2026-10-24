using System;
using System.Collections.Generic;

namespace Utility.Hashing;

public class Index<TItem, TIndex>
{
	public HashSet<TItem> AllItems;

	public Dictionary<TIndex, List<TItem>> IndexedItems;

	private readonly Func<TItem, TIndex> _getIndexedProperty;

	public Index(Func<TItem, TIndex> getProperty)
	{
		_getIndexedProperty = getProperty;
		AllItems = new HashSet<TItem>();
		IndexedItems = new Dictionary<TIndex, List<TItem>>();
	}

	public Index(Func<TItem, TIndex> getProperty, IEqualityComparer<TItem> equalityComparer)
	{
		_getIndexedProperty = getProperty;
		AllItems = new HashSet<TItem>(equalityComparer);
		IndexedItems = new Dictionary<TIndex, List<TItem>>();
	}

	public Index(Func<TItem, TIndex> getProperty, HashSet<TItem> itemCollection)
	{
		_getIndexedProperty = getProperty;
		AllItems = itemCollection;
		IndexedItems = new Dictionary<TIndex, List<TItem>>();
	}

	public bool Add(TItem actualValue2)
	{
		bool result = false;
		TIndex key = _getIndexedProperty(actualValue2);
		if (!AllItems.TryGetValue(actualValue2, out var actualValue))
		{
			AllItems.Add(actualValue2);
			result = true;
			actualValue = actualValue2;
		}
		if (IndexedItems.TryGetValue(key, out var indexedItems))
		{
			indexedItems.Add(actualValue);
		}
		else
		{
			indexedItems = new List<TItem>();
			indexedItems.Add(actualValue);
			IndexedItems[key] = indexedItems;
		}
		return result;
	}

	public List<TItem> GetItems(TIndex propValue)
	{
		if (IndexedItems.TryGetValue(propValue, out var localValue))
		{
			return localValue;
		}
		return new List<TItem>();
	}
}
