using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Utility.Unsafe;

public class UnsafeHelpers
{
	private static class UnsafeT<T>
	{
		[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_items")]
		public static extern ref T[] GetListRawBuffer(List<T> items);

		[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_size")]
		public static extern ref int GetListSize(List<T> items);

		[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_version")]
		public static extern ref int GetListVersion(List<T> items);

		[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Grow")]
		public static extern void Grow(List<T> items, int toCapacity);

		public static Memory<T> EnsureNewSpace(List<T> items, int length)
		{
			int count = items.Count;
			items.EnsureCapacity(count + length);
			GetListSize(items) += length;
			return GetListRawBuffer(items).AsMemory(count, length);
		}

		public static Memory<T> GetListBackingBuffer(List<T> lst)
		{
			return GetListRawBuffer(lst).AsMemory(0, lst.Count);
		}

		public static void SetListBackingBuffer(List<T> items, T[] data, int useCount = -1)
		{
			GetListRawBuffer(items) = data;
			ref int listSize = ref GetListSize(items);
			if (useCount >= 0)
			{
				listSize = useCount;
			}
			else
			{
				listSize = data.Length;
			}
		}
	}

	public static Memory<T> GetListBackingBuffer<T>(List<T> lst)
	{
		return UnsafeT<T>.GetListBackingBuffer(lst);
	}

	public static void SetListBackingBuffer<T>(List<T> lst, T[] buffer, int useCount = -1)
	{
		UnsafeT<T>.SetListBackingBuffer(lst, buffer, useCount);
	}

	public static T[] GetListRawBackingBuffer<T>(List<T> lst)
	{
		return UnsafeT<T>.GetListRawBuffer(lst);
	}

	public static List<T> NewListFromBuffer<T>(T[] buffer, int useCount = -1)
	{
		List<T> items = new List<T>();
		SetListBackingBuffer(items, buffer, useCount);
		return items;
	}

	public static Memory<T> EnsureNewSpace<T>(List<T> items, int length)
	{
		return UnsafeT<T>.EnsureNewSpace(items, length);
	}

	public static Memory<int> GetBitArrayBackingBuffer(BitArray ba, bool sliceToSize = true)
	{
		throw new NotImplementedException();
	}

	public static void SetBitArrayBackingBuffer(BitArray ba, int[] buffer, int bitCount)
	{
		throw new NotImplementedException();
	}
}
