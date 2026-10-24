using System.Collections.Generic;
using System.Collections.Specialized;

namespace Utility.DataStructures;

public class ObservableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, INotifyCollectionChanged
{
	public new TValue this[TKey key]
	{
		get
		{
			return base[key];
		}
		set
		{
			KeyValuePair<TKey, TValue> replaceIds = default;
			if (ContainsKey(key))
			{
				replaceIds = new KeyValuePair<TKey, TValue>(key, base[key]);
			}
			base[key] = value;
			KeyValuePair<TKey, TValue> replaceIds2 = new KeyValuePair<TKey, TValue>(key, base[key]);
			CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, replaceIds2, replaceIds));
		}
	}

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	public new void Add(TKey key, TValue tValue)
	{
		base.Add(key, tValue);
		KeyValuePair<TKey, TValue> addIds = new KeyValuePair<TKey, TValue>(key, tValue);
		CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, addIds));
	}

	public new bool Remove(TKey key)
	{
		KeyValuePair<TKey, TValue> removeIds = new KeyValuePair<TKey, TValue>(key, this[key]);
		bool result = base.Remove(key);
		CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removeIds));
		return result;
	}

	public new void Clear()
	{
		base.Clear();
		CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}
}
