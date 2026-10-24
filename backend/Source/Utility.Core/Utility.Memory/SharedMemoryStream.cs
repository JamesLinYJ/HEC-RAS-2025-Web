using System;
using System.Buffers;
using System.IO;

namespace Utility.Memory;

public sealed class SharedMemoryStream : Stream
{
	private IMemoryOwner<byte> _memoryOwner;

	private Memory<byte> _memory;

	private int _length;

	private int _position;

	private bool _disposed;

	public override bool CanRead => !_disposed;

	public override bool CanSeek => !_disposed;

	public override bool CanWrite => !_disposed;

	public override long Length => _length;

	public override long Position
	{
		get
		{
			return _position;
		}
		set
		{
			if (value < 0 || value > _length)
			{
				throw new ArgumentOutOfRangeException("value");
			}
			_position = (int)value;
		}
	}

	public SharedMemoryStream(int capacity = 1024)
	{
		_memoryOwner = MemoryPool<byte>.Shared.Rent(capacity);
		_memory = _memoryOwner.Memory;
		_length = 0;
		_position = 0;
	}

	public void TakeOwnership(out IMemoryOwner<byte> owner, out Memory<byte> slicedMem)
	{
		owner = _memoryOwner;
		slicedMem = _memory.Slice(0, _length);
		_memory = Memory<byte>.Empty;
		_memoryOwner = null;
		_disposed = true;
	}

	public override void Flush()
	{
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("SharedMemoryStream");
		}
		int lengthAbovePosition = _length - _position;
		if (lengthAbovePosition <= 0)
		{
			return 0;
		}
		int countTerm = Math.Min(count, lengthAbovePosition);
		_memory.Span.Slice(_position, countTerm).CopyTo(buffer.AsSpan(offset, countTerm));
		_position += countTerm;
		return countTerm;
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("SharedMemoryStream");
		}
		EnsureCapacity(_position + count);
		buffer.AsSpan(offset, count).CopyTo(_memory.Span.Slice(_position));
		_position += count;
		if (_position > _length)
		{
			_length = _position;
		}
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("SharedMemoryStream");
		}
		int position = origin switch
		{
			SeekOrigin.Begin => (int)offset, 
			SeekOrigin.Current => _position + (int)offset, 
			SeekOrigin.End => _length + (int)offset, 
			_ => throw new ArgumentOutOfRangeException("origin"), 
		};
		if (position < 0 || position > _length)
		{
			throw new IOException("Seek before beginning or after end.");
		}
		_position = position;
		return _position;
	}

	public override void SetLength(long value)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("SharedMemoryStream");
		}
		if (value < 0 || value > int.MaxValue)
		{
			throw new ArgumentOutOfRangeException("value");
		}
		EnsureCapacity((int)value);
		_length = (int)value;
		if (_position > _length)
		{
			_position = _length;
		}
	}

	private void EnsureCapacity(int required)
	{
		if (required > _memory.Length)
		{
			int minBufferSize = Math.Max(required, _memory.Length * 2);
			IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(minBufferSize);
			_memory.Span.Slice(0, _length).CopyTo(memoryOwner.Memory.Span);
			_memoryOwner.Dispose();
			_memoryOwner = memoryOwner;
			_memory = memoryOwner.Memory;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (!_disposed)
		{
			if (disposing)
			{
				_memoryOwner?.Dispose();
			}
			_disposed = true;
		}
		base.Dispose(disposing);
	}
}
