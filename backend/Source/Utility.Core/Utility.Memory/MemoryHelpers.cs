using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Utility.Memory;

public static class MemoryHelpers
{
	public static Span<T> CreateMirroredMemory<T>(int ct, out Memory<byte> other) where T : unmanaged
	{
		int sizeOf = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
		other = new byte[ct * sizeOf];
		return MemoryMarshal.Cast<byte, T>(other.Span);
	}
}
