using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Utility.Memory;

public sealed class CastMemoryStream<T> : Stream where T : unmanaged
{
	private readonly Memory<T> _data;

	private int _position;

	private readonly int _szT;

	public override bool CanRead => true;

	public override bool CanSeek => true;

	public override bool CanWrite => true;

	public override long Length => _data.Length * _szT;

	public override long Position
	{
		get
		{
			return _position;
		}
		set
		{
			if (value < 0 || value > Length)
			{
				throw new ArgumentOutOfRangeException("value");
			}
			_position = (int)value;
		}
	}

	public CastMemoryStream(Memory<T> buffer)
	{
		_data = buffer;
		_position = 0;
		_szT = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
	}

	public override void Flush()
	{
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		ValidateBufferArgs(buffer, offset, count);
		int countAvailableTerm = Math.Min(count, Available());
		if (countAvailableTerm <= 0)
		{
			return 0;
		}
		MemoryMarshal.AsBytes(_data.Span).Slice(_position, countAvailableTerm).CopyTo(buffer.AsSpan(offset, countAvailableTerm));
		_position += countAvailableTerm;
		return countAvailableTerm;
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		ValidateBufferArgs(buffer, offset, count);
		int available = Available();
		if (count > available)
		{
			throw new InvalidOperationException("Not enough space to write.");
		}
		Span<byte> asBytes = MemoryMarshal.AsBytes(_data.Span);
		buffer.AsSpan(offset, count).CopyTo(asBytes.Slice(_position, count));
		_position += count;
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		int position = origin switch
		{
			SeekOrigin.Begin => (int)offset, 
			SeekOrigin.Current => _position + (int)offset, 
			SeekOrigin.End => (int)(Length + offset), 
			_ => throw new ArgumentException("Invalid SeekOrigin", "origin"), 
		};
		if (position < 0 || position > Length)
		{
			throw new IOException("Attempted to seek outside the stream.");
		}
		_position = position;
		return _position;
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override int Read(Span<byte> buffer)
	{
		int availableTerm = Math.Min(buffer.Length, Available());
		if (availableTerm <= 0)
		{
			return 0;
		}
		MemoryMarshal.AsBytes(_data.Span).Slice(_position, availableTerm).CopyTo(buffer);
		_position += availableTerm;
		return availableTerm;
	}

	private int Available()
	{
		return (int)(Length - _position);
	}

	public override void Write(ReadOnlySpan<byte> buffer)
	{
		int available = Available();
		if (buffer.Length > available)
		{
			throw new InvalidOperationException("Not enough space to write.");
		}
		buffer.CopyTo(MemoryMarshal.AsBytes(_data.Span).Slice(_position, buffer.Length));
		_position += buffer.Length;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		return Task.FromResult(Read(buffer, offset, count));
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		Write(buffer, offset, count);
		return Task.CompletedTask;
	}

	private static void ValidateBufferArgs(byte[] buffer, int offset, int count)
	{
		if (buffer == null)
		{
			throw new ArgumentNullException("buffer");
		}
		if ((uint)offset > buffer.Length || (uint)count > buffer.Length - offset)
		{
			throw new ArgumentOutOfRangeException("offset/count");
		}
	}
}
