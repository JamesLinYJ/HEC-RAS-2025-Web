using System;

namespace Utility.Memory;

public static class ValueSharedMemoryPool<T> where T : unmanaged
{
	public static PooledArray<T> Rent(int sz, out Memory<T> mem)
	{
		PooledArray<T> result = new PooledArray<T>(sz);
		mem = result.Array.AsMemory(0, sz);
		mem.Span.Clear();
		return result;
	}

	public static PooledArray<T> Rent(int sz, T flushValue, out Memory<T> mem)
	{
		PooledArray<T> result = new PooledArray<T>(sz);
		mem = result.Array.AsMemory(0, sz);
		mem.Span.Fill(flushValue);
		return result;
	}

	public static PooledArray<T> RentNoFill(int sz, out Memory<T> mem)
	{
		PooledArray<T> result = new PooledArray<T>(sz);
		mem = result.Array.AsMemory(0, sz);
		return result;
	}
}
