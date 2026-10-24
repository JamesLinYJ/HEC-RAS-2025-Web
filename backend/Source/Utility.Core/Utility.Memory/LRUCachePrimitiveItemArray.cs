using System;
using System.Runtime.InteropServices;

namespace Utility.Memory;

public class LRUCachePrimitiveItemArray<T> : ITimedCacheItem where T : struct
{
	private T[] _item;

	public long LastAccessedTime { get; private set; }

	public long ItemSize { get; private set; }

	public T[] Item
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

	public LRUCachePrimitiveItemArray(T[] elements)
	{
		_item = elements;
		LastAccessedTime = DateTime.Now.Ticks;
		ItemSize = Marshal.SizeOf(typeof(T)) * elements.Length;
	}

	public static implicit operator T[](LRUCachePrimitiveItemArray<T> cacheItem)
	{
		return cacheItem.Item;
	}

	public static implicit operator LRUCachePrimitiveItemArray<T>(T[] elements)
	{
		return new LRUCachePrimitiveItemArray<T>(elements);
	}

	public override bool Equals(object obj)
	{
		return Item.Equals(obj);
	}

	public static bool operator ==(LRUCachePrimitiveItemArray<T> lhs, T[] rhs)
	{
		if ((object)lhs == null || lhs.Item == null)
		{
			if (rhs == null)
			{
				return true;
			}
			return false;
		}
		return lhs.Item.Equals(rhs);
	}

	public static bool operator !=(LRUCachePrimitiveItemArray<T> lhs, T[] rhs)
	{
		if ((object)lhs == null || lhs.Item == null)
		{
			if (rhs == null)
			{
				return false;
			}
			return true;
		}
		return !lhs.Item.Equals(rhs);
	}

	public override int GetHashCode()
	{
		return base.GetHashCode();
	}
}
