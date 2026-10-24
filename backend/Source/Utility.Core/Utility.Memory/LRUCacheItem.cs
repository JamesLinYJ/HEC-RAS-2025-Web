using System;
using System.Collections.Generic;

namespace Utility.Memory;

public class LRUCacheItem<T> : ITimedCacheItem where T : ISizeKnown
{
	private T _item;

	public long LastAccessedTime { get; private set; }

	public long ItemSize
	{
		get
		{
			if (_item != null)
			{
				return _item.Size;
			}
			return 0L;
		}
	}

	public T Item
	{
		get
		{
			LastAccessedTime = DateTime.Now.Ticks;
			return _item;
		}
		set
		{
			LastAccessedTime = DateTime.Now.Ticks;
			_item = value;
		}
	}

	public LRUCacheItem(T localValue)
	{
		_item = localValue;
		LastAccessedTime = DateTime.Now.Ticks;
	}

	public static implicit operator T(LRUCacheItem<T> cacheItem)
	{
		return cacheItem.Item;
	}

	public static implicit operator LRUCacheItem<T>(T localValue)
	{
		return new LRUCacheItem<T>(localValue);
	}

	public override bool Equals(object obj)
	{
		return Item.Equals(obj);
	}

	public static bool operator ==(LRUCacheItem<T> lhs, T rhs)
	{
		if (lhs == null || lhs.Item == null)
		{
			if (rhs == null)
			{
				return true;
			}
			return false;
		}
		return EqualityComparer<T>.Default.Equals(lhs.Item, rhs);
	}

	public static bool operator !=(LRUCacheItem<T> lhs, T rhs)
	{
		if (lhs == null || lhs.Item == null)
		{
			if (rhs == null)
			{
				return false;
			}
			return true;
		}
		return !EqualityComparer<T>.Default.Equals(lhs.Item, rhs);
	}

	public override int GetHashCode()
	{
		return base.GetHashCode();
	}
}
