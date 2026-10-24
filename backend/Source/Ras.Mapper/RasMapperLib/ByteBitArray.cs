using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RasMapperLib;

public class ByteBitArray
{
	private int _countOn;

	private int _length;

	private byte[] _array;

	public int CountOn => _countOn;

	public bool this[int index]
	{
		get
		{
			if (index < 0 || index >= _length)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			return (_array[index / 8] & (1 << index % 8)) != 0;
		}
		set
		{
			if (index < 0 || index >= _length)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			checked
			{
				if (value)
				{
					_array[unchecked(index / 8)] = (byte)unchecked(_array[index / 8] | (1 << index % 8));
				}
				else
				{
					_array[unchecked(index / 8)] = (byte)unchecked(_array[index / 8] & ~(1 << index % 8));
				}
			}
		}
	}

	public ByteBitArray(int length)
	{
		_countOn = 0;
		_length = length;
		checked
		{
			int ordinal = (int)Math.Ceiling((double)_length / 8.0);
			if (ordinal < 4)
			{
				ordinal = 4;
			}
			_array = new byte[ordinal - 1 + 1];
		}
	}

	public ByteBitArray(ByteBitCompressedData cd)
		: this(cd.Length)
	{
		_countOn = cd.CountOn;
		using MemoryStream stream = new MemoryStream(cd.CompressedBytes);
		using DeflateStream deflateStream = new DeflateStream(stream, CompressionMode.Decompress);
		deflateStream.ReadExactly(_array, 0, _array.Count());
	}

	public ByteBitCompressedData CompressedData()
	{
		byte[] byteValues;
		using (MemoryStream memoryStream = new MemoryStream())
		{
			using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
			{
				deflateStream.Write(_array, 0, _array.Length);
			}
			byteValues = new byte[checked((int)(memoryStream.Length - 1) + 1)];
			Buffer.BlockCopy(memoryStream.GetBuffer(), 0, byteValues, 0, byteValues.Count());
		}
		return new ByteBitCompressedData(_countOn, _length, byteValues);
	}

	public void SetAll(bool all)
	{
		byte b = (byte)(all ? byte.MaxValue : 0);
		int count = _array.Count();
		for (int i = 0; i <= count; i = checked(i + 1))
		{
			_array[i] = b;
		}
	}
}
