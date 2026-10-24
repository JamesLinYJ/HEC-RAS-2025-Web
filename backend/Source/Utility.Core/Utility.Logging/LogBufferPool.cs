using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace Utility.Logging;

internal static class LogBufferPool
{
	public const int BufferChars = 1024;

	private const int MaxRetained = 8192;

	private static readonly ConcurrentQueue<char[]> _buffers = new ConcurrentQueue<char[]>();

	private static int _retained;

	public static char[] Rent(int minimumLength)
	{
		if (minimumLength > 1024)
		{
			return ArrayPool<char>.Shared.Rent(minimumLength);
		}
		if (!_buffers.TryDequeue(out var result))
		{
			return new char[1024];
		}
		Interlocked.Decrement(ref _retained);
		return result;
	}

	public static void Return(char[] buffer)
	{
		if (buffer != null)
		{
			if (buffer.Length != 1024)
			{
				ArrayPool<char>.Shared.Return(buffer);
			}
			else if (Volatile.Read(in _retained) < 8192)
			{
				Interlocked.Increment(ref _retained);
				_buffers.Enqueue(buffer);
			}
		}
	}
}
