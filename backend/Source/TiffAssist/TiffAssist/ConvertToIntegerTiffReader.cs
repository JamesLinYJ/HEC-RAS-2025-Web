using System;
using System.IO;
using System.Threading;
using BitMiracle.LibTiff.Classic;

namespace TiffAssist;

public class ConvertToIntegerTiffReader : IDisposable
{
	private readonly ByteTiffReader _btr;

	private readonly TiffReader<short> _str;

	private readonly IntTiffReader _itr;

	private readonly ThreadLocal<byte[]> _byteBuf;

	private readonly ThreadLocal<short[]> _shortBuf;

	private readonly int _tileSize;

	public int TileSize => _tileSize;

	public ConvertToIntegerTiffReader(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		using TiffReaderBase tiffReaderBase = new TiffReaderBase(filename);
		SampleFormat format = TiffBase.GetFormat(filename);
		if (format != SampleFormat.INT && format != SampleFormat.UINT)
		{
			throw new NotImplementedException("File needs to be an integer type (byte/short/int/long) at the moment.");
		}
		_tileSize = tiffReaderBase.GetTileHeight(0);
		int tileWidth = tiffReaderBase.GetTileWidth(0);
		if (_tileSize != tileWidth)
		{
			throw new Exception("Malformed tiff, TileWidth != TileHeight");
		}
		switch (TiffBase.BitsPerSample(filename))
		{
		case 8:
			_btr = new ByteTiffReader(filename);
			_byteBuf = new ThreadLocal<byte[]>(() => new byte[_tileSize * _tileSize]);
			break;
		case 16:
			_str = new TiffReader<short>(filename);
			_shortBuf = new ThreadLocal<short[]>(() => new short[_tileSize * _tileSize]);
			break;
		case 32:
			_itr = new IntTiffReader(filename);
			break;
		default:
			throw new NotSupportedException();
		}
	}

	public static bool CanRead(string filename)
	{
		if (!File.Exists(filename))
		{
			return false;
		}
		try
		{
			using TiffReaderBase tiffReaderBase = new TiffReaderBase(filename);
			SampleFormat format = tiffReaderBase.GetFormat();
			if (format != SampleFormat.INT && format != SampleFormat.UINT)
			{
				return false;
			}
			int bitsPerSample = tiffReaderBase.BitsPerSample();
			if (bitsPerSample == 8 || bitsPerSample == 16 || bitsPerSample == 32)
			{
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public TiffReaderBase GetReader()
	{
		if (_btr != null)
		{
			return _btr;
		}
		if (_str != null)
		{
			return _str;
		}
		if (_itr != null)
		{
			return _itr;
		}
		throw new NotSupportedException();
	}

	public int GetNoData()
	{
		return ((int?)_btr?.GetNoData(0)) ?? ((int?)_str?.GetNoData(0)) ?? _itr?.GetNoData(0) ?? 0;
	}

	public void GetTileFillOOB(int DirectoryNumber, int TileNumber, int fillValue, Memory<int> arr)
	{
		if (arr.Length != _tileSize * _tileSize)
		{
			throw new ArgumentException();
		}
		if (_btr != null)
		{
			byte[] byteBufTerm = _byteBuf.Value;
			_btr.GetTileFillOOB(DirectoryNumber, TileNumber, (byte)fillValue, byteBufTerm);
			Span<int> arrSpan = arr.Span;
			for (int i = 0; i < byteBufTerm.Length; i++)
			{
				arrSpan[i] = byteBufTerm[i];
			}
		}
		else if (_str != null)
		{
			short[] shorts = _shortBuf.Value;
			_str.GetTileFillOOB(DirectoryNumber, TileNumber, (short)fillValue, shorts);
			Span<int> arr2 = arr.Span;
			for (int j = 0; j < shorts.Length; j++)
			{
				arr2[j] = shorts[j];
			}
		}
		else
		{
			if (_itr == null)
			{
				throw new NotSupportedException();
			}
			_itr.GetTileFillOOB(DirectoryNumber, TileNumber, fillValue, arr);
		}
	}

	public void Dispose()
	{
		_btr?.Dispose();
		_str?.Dispose();
		_itr?.Dispose();
	}
}
