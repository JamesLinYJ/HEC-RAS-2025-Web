using System;
using System.Buffers;

namespace Utility.Memory;

public static class SharedMemoryPool<T> where T : unmanaged
{
	public static IMemoryOwner<T> Rent(int sz)
	{
		LogRequest(sz);
		IMemoryOwner<T> memoryOwner = MemoryPool<T>.Shared.Rent(sz);
		memoryOwner.Memory.Span.Fill(default);
		return memoryOwner;
	}

	public static IMemoryOwner<T> Rent(int sz, out Memory<T> mem)
	{
		LogRequest(sz);
		IMemoryOwner<T> memoryOwner = MemoryPool<T>.Shared.Rent(sz);
		mem = memoryOwner.Memory.Slice(0, sz);
		mem.Span.Fill(default);
		return memoryOwner;
	}

	public static IMemoryOwner<T> RentNoFill(int sz, out Memory<T> mem)
	{
		LogRequest(sz);
		IMemoryOwner<T> memoryOwner = MemoryPool<T>.Shared.Rent(sz);
		mem = memoryOwner.Memory.Slice(0, sz);
		return memoryOwner;
	}

	public static IMemoryOwner<T> Rent(int sz, T flushValue)
	{
		LogRequest(sz);
		IMemoryOwner<T> memoryOwner = MemoryPool<T>.Shared.Rent(sz);
		memoryOwner.Memory.Slice(0, sz).Span.Fill(flushValue);
		return memoryOwner;
	}

	public static IMemoryOwner<T> Rent(int sz, T flushValue, out Memory<T> mem)
	{
		LogRequest(sz);
		IMemoryOwner<T> memoryOwner = MemoryPool<T>.Shared.Rent(sz);
		mem = memoryOwner.Memory.Slice(0, sz);
		mem.Span.Fill(flushValue);
		return memoryOwner;
	}

	private static void LogRequest(int sz)
	{
	}
}
