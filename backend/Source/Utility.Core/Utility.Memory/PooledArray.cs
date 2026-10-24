using System;
using System.Buffers;

namespace Utility.Memory;

public ref struct PooledArray<T>(int minLength) : IDisposable
{
	private T[] _array = ArrayPool<T>.Shared.Rent(minLength);

	public T[] Array => _array;

	public Span<T> AsSpan(int start, int length)
	{
		return _array.AsSpan(start, length);
	}

	public void Dispose()
	{
		if (_array != null)
		{
			ArrayPool<T>.Shared.Return(_array);
		}
		_array = null;
	}
}
