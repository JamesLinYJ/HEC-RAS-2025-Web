using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Utility.Memory;

public sealed class ByteBackedIntMemory : MemoryManager<int>
{
	private byte[] _bytes;

	private readonly int _intLength;

	public byte[] Bytes => _bytes;

	public ByteBackedIntMemory(int intLength)
	{
		_intLength = intLength;
		_bytes = ArrayPool<byte>.Shared.Rent(intLength * 4);
	}

	public override Span<int> GetSpan()
	{
		return MemoryMarshal.Cast<byte, int>(_bytes.AsSpan(0, _intLength * 4));
	}

	public override MemoryHandle Pin(int elementIndex = 0)
	{
		throw new NotSupportedException();
	}

	public override void Unpin()
	{
		throw new NotSupportedException();
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected override void Dispose(bool disposing)
	{
		if (_bytes != null)
		{
			ArrayPool<byte>.Shared.Return(_bytes);
			_bytes = null;
		}
	}
}
