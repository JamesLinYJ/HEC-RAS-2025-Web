using System;
using System.Collections.Generic;

namespace Utility.Memory;

public class WriteCache<TKey, TValue> : CacheManager<TKey, TValue> where TValue : class
{
	private readonly HashSet<TKey> _dirty = new HashSet<TKey>();

	private readonly Action<TKey, TValue> _flush;

	public WriteCache(Action<TKey, TValue> flush, Func<TValue, int> getSizeInBytes)
		: base(getSizeInBytes, (IEqualityComparer<TKey>)null, true)
	{
		_flush = flush;
		OnItemEvicted += WriteCache_OnItemEvicted;
	}

	private void WriteCache_OnItemEvicted(TKey key, TValue tValue)
	{
		bool dirtyTerm = false;
		lock (_dirty)
		{
			dirtyTerm = _dirty.Remove(key);
		}
		if (dirtyTerm)
		{
			_flush(key, tValue);
			Log(CacheAction.Flush, key);
		}
	}

	public override void Set(TKey key, TValue tValue)
	{
		lock (_dirty)
		{
			_dirty.Add(key);
		}
		base.Set(key, tValue);
	}
}
