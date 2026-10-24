using System;
using System.IO;

namespace Utility.Memory;

public class CustomMemoryStream : Stream
{
	private byte[] _buffer;

	private int _position;

	private int _length;

	private int _capacity;

	public override bool CanRead => true;

	public override bool CanSeek => true;

	public override bool CanWrite => true;

	public override long Length => _length;

	public override long Position
	{
		get
		{
			return _position;
		}
		set
		{
			_position = (int)value;
		}
	}

	public virtual int Capacity
	{
		get
		{
			return _capacity;
		}
		set
		{
			if (_capacity != value)
			{
				byte[] byteValues = new byte[value];
				if (_length > 0)
				{
					System.Buffer.BlockCopy(_buffer, 0, byteValues, 0, _length);
				}
				_buffer = byteValues;
				_capacity = value;
			}
		}
	}

	public byte[] Buffer => _buffer;

	public CustomMemoryStream(byte[] buffer)
	{
		_buffer = buffer;
		_position = 0;
		_length = 0;
		_capacity = buffer.Length;
	}

	public CustomMemoryStream(byte[] buffer, int startPos)
	{
		_buffer = buffer;
		_position = startPos;
		_length = startPos;
		_capacity = buffer.Length;
	}

	private bool EnsureCapacity(int newSize)
	{
		if (newSize > _capacity)
		{
			int newSize2 = newSize;
			if (newSize2 < 256)
			{
				newSize2 = 256;
			}
			if (newSize2 < _capacity * 2)
			{
				newSize2 = _capacity * 2;
			}
			Capacity = newSize2;
			return true;
		}
		return false;
	}

	public void AddLength(int addLen)
	{
		SetLength(Length + addLen);
	}

	public override void Flush()
	{
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		int countLengthTerm = Math.Min(count, _length - _position);
		if (countLengthTerm == 0)
		{
			return 0;
		}
		System.Buffer.BlockCopy(_buffer, _position, buffer, offset, countLengthTerm);
		_position += countLengthTerm;
		return countLengthTerm;
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		switch (origin)
		{
		case SeekOrigin.Begin:
			_position = (int)offset;
			break;
		case SeekOrigin.Current:
			_position += (int)offset;
			break;
		case SeekOrigin.End:
			throw new NotSupportedException();
		}
		return _position;
	}

	public override void SetLength(long newLen)
	{
		if (newLen > _capacity)
		{
			Capacity = (int)newLen;
		}
		_length = (int)newLen;
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		int newSize = _position + count;
		EnsureCapacity(newSize);
		System.Buffer.BlockCopy(buffer, offset, _buffer, _position, count);
		_position += count;
		if (_position > _length)
		{
			_length = _position;
		}
	}
}
