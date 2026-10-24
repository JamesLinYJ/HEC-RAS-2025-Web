using System;

namespace Utility.Memory;

public class CacheItem<T> : ICachedElement
{
	public int Pinned;

	public long LastAccessTime { get; set; }

	public T Item { get; set; }

	public CacheItem(T localValue)
	{
		Item = localValue;
		LastAccessTime = DateTime.Now.Ticks;
	}
}
