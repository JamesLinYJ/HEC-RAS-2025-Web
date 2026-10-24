using System;

namespace Utility.Memory;

public class LRUCacheDisposableItem<T> : LRUCacheItem<T>, IDisposable where T : ISizeKnown, IDisposable
{
	public LRUCacheDisposableItem(T localValue)
		: base(localValue)
	{
	}

	public void Dispose()
	{
		Item.Dispose();
	}

	public static implicit operator T(LRUCacheDisposableItem<T> cacheItem)
	{
		return cacheItem.Item;
	}

	public static implicit operator LRUCacheDisposableItem<T>(T localValue)
	{
		return new LRUCacheDisposableItem<T>(localValue);
	}
}
