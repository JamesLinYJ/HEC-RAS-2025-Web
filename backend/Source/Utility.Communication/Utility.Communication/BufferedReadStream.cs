using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Utility.Communication;

public class BufferedReadStream : Stream
{
	private readonly Stream _innerStream;

	private readonly bool _ownsInnerStream;

	private readonly int _bufferSize;

	private byte[] _buffer;

	private long _bufferStart = -1L;

	private int _bufferValidLength;

	private long _position;

	public const int DefaultBufferSize = 262144;

	public override bool CanRead => true;

	public override bool CanSeek => _innerStream.CanSeek;

	public override bool CanWrite => false;

	public override long Length => _innerStream.Length;

	public override long Position
	{
		get
		{
			return _position;
		}
		set
		{
			_position = value;
		}
	}

	public BufferedReadStream(Stream innerStream, int bufferSize = 262144, bool ownsInnerStream = true)
	{
		_innerStream = innerStream ?? throw new ArgumentNullException("innerStream");
		_bufferSize = bufferSize;
		_ownsInnerStream = ownsInnerStream;
		_buffer = new byte[bufferSize];
		_position = innerStream.Position;
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		ValidateReadArgs(buffer, offset, count);
		if (count == 0)
		{
			return 0;
		}
		if (TryReadFromBuffer(buffer, offset, count, out var bytesRead))
		{
			_position += bytesRead;
			return bytesRead;
		}
		FillBuffer(_position);
		if (TryReadFromBuffer(buffer, offset, count, out bytesRead))
		{
			_position += bytesRead;
			return bytesRead;
		}
		return 0;
	}

	public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default(CancellationToken))
	{
		ValidateReadArgs(buffer, offset, count);
		if (count == 0)
		{
			return 0;
		}
		if (TryReadFromBuffer(buffer, offset, count, out var bytesRead))
		{
			_position += bytesRead;
			return bytesRead;
		}
		await FillBufferAsync(_position, ct);
		if (TryReadFromBuffer(buffer, offset, count, out bytesRead))
		{
			_position += bytesRead;
			return bytesRead;
		}
		return 0;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default(CancellationToken))
	{
		if (buffer.Length == 0)
		{
			return 0;
		}
		if (TryReadFromBuffer(buffer.Span, out var bytesRead))
		{
			_position += bytesRead;
			return bytesRead;
		}
		await FillBufferAsync(_position, ct);
		if (TryReadFromBuffer(buffer.Span, out bytesRead))
		{
			_position += bytesRead;
			return bytesRead;
		}
		return 0;
	}

	private bool TryReadFromBuffer(byte[] buffer, int offset, int count, out int bytesRead)
	{
		bytesRead = 0;
		if (_bufferStart < 0 || _bufferValidLength == 0)
		{
			return false;
		}
		long bufferStartPlusBufferValidLength = _bufferStart + _bufferValidLength;
		if (_position < _bufferStart || _position >= bufferStartPlusBufferValidLength)
		{
			return false;
		}
		int positionAboveBufferStart = (int)(_position - _bufferStart);
		int bufferValidLengthAbovePositionAboveBufferStart = _bufferValidLength - positionAboveBufferStart;
		bytesRead = Math.Min(count, bufferValidLengthAbovePositionAboveBufferStart);
		Buffer.BlockCopy(_buffer, positionAboveBufferStart, buffer, offset, bytesRead);
		return true;
	}

	private bool TryReadFromBuffer(Span<byte> destination, out int bytesRead)
	{
		bytesRead = 0;
		if (_bufferStart < 0 || _bufferValidLength == 0)
		{
			return false;
		}
		long bufferStartPlusBufferValidLength = _bufferStart + _bufferValidLength;
		if (_position < _bufferStart || _position >= bufferStartPlusBufferValidLength)
		{
			return false;
		}
		int positionAboveBufferStart = (int)(_position - _bufferStart);
		int bufferValidLengthAbovePositionAboveBufferStart = _bufferValidLength - positionAboveBufferStart;
		bytesRead = Math.Min(destination.Length, bufferValidLengthAbovePositionAboveBufferStart);
		_buffer.AsSpan(positionAboveBufferStart, bytesRead).CopyTo(destination);
		return true;
	}

	private void FillBuffer(long filePosition)
	{
		long lengthAboveFilePosition = Length - filePosition;
		int bufferSizeTerm = (int)Math.Min(_bufferSize, lengthAboveFilePosition);
		if (bufferSizeTerm <= 0)
		{
			_bufferStart = -1L;
			_bufferValidLength = 0;
		}
		else
		{
			_innerStream.Position = filePosition;
			_bufferValidLength = _innerStream.Read(_buffer, 0, bufferSizeTerm);
			_bufferStart = filePosition;
		}
	}

	private async Task FillBufferAsync(long filePosition, CancellationToken ct)
	{
		long lengthAboveFilePosition = Length - filePosition;
		int toRead = (int)Math.Min(_bufferSize, lengthAboveFilePosition);
		if (toRead <= 0)
		{
			_bufferStart = -1L;
			_bufferValidLength = 0;
			return;
		}
		_innerStream.Position = filePosition;
		int totalRead;
		int readAsync;
		for (totalRead = 0; totalRead < toRead; totalRead += readAsync)
		{
			readAsync = await _innerStream.ReadAsync(_buffer.AsMemory(totalRead, toRead - totalRead), ct);
			if (readAsync == 0)
			{
				break;
			}
		}
		_bufferValidLength = totalRead;
		_bufferStart = filePosition;
	}

	private static void ValidateReadArgs(byte[] buffer, int offset, int count)
	{
		if (buffer == null)
		{
			throw new ArgumentNullException("buffer");
		}
		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		if (count < 0)
		{
			throw new ArgumentOutOfRangeException("count");
		}
		if (offset + count > buffer.Length)
		{
			throw new ArgumentException("Offset and count exceed buffer length.");
		}
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		_position = origin switch
		{
			SeekOrigin.Begin => offset, 
			SeekOrigin.Current => _position + offset, 
			SeekOrigin.End => Length + offset, 
			_ => throw new ArgumentOutOfRangeException("origin"), 
		};
		return _position;
	}

	public override void Flush()
	{
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _ownsInnerStream)
		{
			_innerStream.Dispose();
		}
		base.Dispose(disposing);
	}

	public override async ValueTask DisposeAsync()
	{
		if (_ownsInnerStream)
		{
			await _innerStream.DisposeAsync();
		}
		await base.DisposeAsync();
	}
}
