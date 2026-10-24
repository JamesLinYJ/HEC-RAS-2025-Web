using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using BitMiracle.LibTiff.Classic;
using Geospatial.Rasters;
using Utility;

namespace TiffAssist;

public class TiffReader<T> : TiffReaderBase, IDisposable where T : unmanaged, IEquatable<T>
{
	protected byte[] _buffer;

	protected static int SizeOfT = Marshal.SizeOf<T>();

	public TiffReader(string TiffFileName)
		: base(TiffFileName, "r", deleteExistingFile: false)
	{
	}

	public T GetNoData(int directory)
	{
		try
		{
			T nodata = default;
			if (TryGetNoData(directory, ref nodata))
			{
				return nodata;
			}
			return GetNaNOrDefault();
		}
		catch
		{
			return GetNaNOrDefault();
		}
	}

	public bool TryGetNoData(int directory, ref T nodata)
	{
		try
		{
			if (!TryGetTag(directory, CustomTags.NODATA, out string tagValue))
			{
				return false;
			}
			nodata = (T)Convert.ChangeType(tagValue, typeof(T));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private T GetNaNOrDefault()
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(float))
		{
			return (T)(object)float.NaN;
		}
		if (typeFromHandle == typeof(double))
		{
			return (T)(object)double.NaN;
		}
		return default;
	}

	public TiffMetadata<T> GetTiffMetadata(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		TiffMetadata<T> tiffMetadata = new TiffMetadata<T>(default, RoundTo.None);
		XElement gDALXML = GetGDALXML(DirectoryNumber);
		if (gDALXML != null)
		{
			tiffMetadata.DataType = ParseGdalXMLString(gDALXML, TiffBase.TYPE_NAME);
			tiffMetadata.DataUnits = ParseGdalXMLString(gDALXML, TiffBase.UNITS_NAME);
			string parseGdalXMLString = ParseGdalXMLString(gDALXML, TiffBase.PRECISION_NAME);
			if (!string.IsNullOrEmpty(parseGdalXMLString))
			{
				Enum.TryParse<RoundTo>(parseGdalXMLString, ignoreCase: true, out tiffMetadata.Rounding);
			}
		}
		FieldValue[] field = _tiffImage.GetField(TiffTag.ARTIST);
		if (field != null && field.Length != 0)
		{
			tiffMetadata.Artist = field[0].ToString();
		}
		FieldValue[] field2 = _tiffImage.GetField(TiffTag.SOFTWARE);
		if (field2 != null && field2.Length != 0)
		{
			tiffMetadata.Software = field2[0].ToString();
		}
		FieldValue[] field3 = _tiffImage.GetField(TiffTag.IMAGEDESCRIPTION);
		if (field3 != null && field3.Length != 0)
		{
			tiffMetadata.ImageDescription = field3[0].ToString();
		}
		FieldValue[] field4 = _tiffImage.GetField(TiffTag.TILELENGTH);
		if (field4 != null && field4.Length != 0)
		{
			tiffMetadata.TileHeight = field4[0].ToInt();
		}
		FieldValue[] field5 = _tiffImage.GetField(TiffTag.TILEWIDTH);
		if (field5 != null && field5.Length != 0)
		{
			tiffMetadata.TileWidth = field5[0].ToInt();
		}
		FieldValue[] field6 = _tiffImage.GetField(TiffTag.COMPRESSION);
		if (field6 != null && field6.Length != 0)
		{
			tiffMetadata.CompressionUsed = field6[0].ToString();
		}
		tiffMetadata.NoDataValue = GetNoData(DirectoryNumber);
		return tiffMetadata;
	}

	protected byte[] GetTile(int DirectoryNumber, int TileNumber)
	{
		SetDirectory((short)DirectoryNumber);
		int numberOfTiles = _tiffImage.NumberOfTiles();
		if (TileNumber >= numberOfTiles)
		{
			throw new Exception("Requested tile doesn't exist in this directory.");
		}
		long tileSize = _tiffImage.TileSize();
		if (_buffer == null || _buffer.Length != tileSize)
		{
			_buffer = new byte[tileSize];
		}
		if (_tiffImage.ReadEncodedTile(TileNumber, _buffer, 0, (int)tileSize) == -1)
		{
			throw new Exception("Tile not read correctly: " + _tiffFileName);
		}
		return _buffer;
	}

	public byte[] GetTileRaw(int DirectoryNumber, int TileNumber)
	{
		SetDirectory((short)DirectoryNumber);
		int numberOfTiles = _tiffImage.NumberOfTiles();
		if (TileNumber >= numberOfTiles)
		{
			throw new Exception("Requested tile doesn't exist in this directory.");
		}
		long rawTileSize = _tiffImage.RawTileSize(TileNumber);
		if (_buffer == null || _buffer.Length != rawTileSize)
		{
			_buffer = new byte[rawTileSize];
		}
		if (_tiffImage.ReadRawTile(TileNumber, _buffer, 0, (int)rawTileSize) == -1)
		{
			throw new Exception("Tile not read correctly: " + _tiffFileName);
		}
		return _buffer;
	}

	public void GetTile(int DirectoryNumber, int TileNumber, ref T[] arr)
	{
		byte[] tile = GetTile(DirectoryNumber, TileNumber);
		int count = (int)Math.Ceiling((float)tile.Length / (float)SizeOfT);
		if (arr == null || arr.Length != count)
		{
			arr = new T[count];
		}
		Buffer.BlockCopy(tile, 0, arr, 0, tile.Length);
	}

	public void GetTile(int DirectoryNumber, int TileNumber, ref T[,] arr)
	{
		int tileHeight = GetTileHeight(DirectoryNumber);
		int tileWidth = GetTileWidth(DirectoryNumber);
		byte[] tile = GetTile(DirectoryNumber, TileNumber);
		if (arr == null || arr.GetLength(0) != tileHeight || arr.GetLength(1) != tileWidth)
		{
			arr = new T[tileHeight, tileWidth];
		}
		Buffer.BlockCopy(tile, 0, arr, 0, tile.Length);
	}

	public void GetTileFillOOB(int DirectoryNumber, int TileNumber, T fillValue, Memory<T> arr)
	{
		byte[] tile = GetTile(DirectoryNumber, TileNumber);
		int tileSizeOfTTerm = (int)Math.Ceiling((float)tile.Length / (float)SizeOfT);
		if (arr.Length != tileSizeOfTTerm)
		{
			throw new ArgumentException();
		}
		Buffers.ArrayCopyUnsafe<byte, T>(tile, arr);
		GetValidRegion(DirectoryNumber, TileNumber, out var ValidWidth, out var ValidHeight);
		int tileHeight = GetTileHeight(DirectoryNumber);
		int tileWidth = GetTileWidth(DirectoryNumber);
		if (ValidWidth == tileWidth && ValidHeight == tileHeight)
		{
			return;
		}
		Span<T> arrSpan = arr.Span;
		for (int i = ValidHeight; i < tileHeight; i++)
		{
			for (int j = 0; j < tileWidth; j++)
			{
				arrSpan[i * tileWidth + j] = fillValue;
			}
		}
		for (int k = 0; k < tileHeight; k++)
		{
			for (int l = ValidWidth; l < tileWidth; l++)
			{
				arrSpan[k * tileWidth + l] = fillValue;
			}
		}
	}

	public void GetTileFillOOB(int DirectoryNumber, int TileNumber, T fillValue, ref T[,] arr)
	{
		int tileHeight = GetTileHeight(DirectoryNumber);
		int tileWidth = GetTileWidth(DirectoryNumber);
		byte[] tile = GetTile(DirectoryNumber, TileNumber);
		if (arr == null || arr.GetLength(0) != tileHeight || arr.GetLength(1) != tileWidth)
		{
			arr = new T[tileHeight, tileWidth];
		}
		Buffer.BlockCopy(tile, 0, arr, 0, tile.Length);
		GetValidRegion(DirectoryNumber, TileNumber, out var ValidWidth, out var ValidHeight);
		if (ValidWidth == tileWidth && ValidHeight == tileHeight)
		{
			return;
		}
		for (int i = ValidHeight; i < tileHeight; i++)
		{
			for (int j = 0; j < tileWidth; j++)
			{
				arr[i, j] = fillValue;
			}
		}
		for (int k = 0; k < tileHeight; k++)
		{
			for (int l = ValidWidth; l < tileWidth; l++)
			{
				arr[k, l] = fillValue;
			}
		}
	}

	public XElement GetGDALXML(int directory)
	{
		if (!TryGetTag(directory, CustomTags.GDAL_METADATA, out byte[] tagValue))
		{
			return null;
		}
		byte[] byteValues;
		if (tagValue[^1] == 0)
		{
			byteValues = new byte[tagValue.Length - 1];
			Buffer.BlockCopy(tagValue, 0, byteValues, 0, tagValue.Length - 1);
		}
		else
		{
			byteValues = tagValue;
		}
		using MemoryStream stream = new MemoryStream(byteValues);
		return XElement.Load((Stream)stream);
	}

	protected static double ParseGdalXMLDouble(XElement root, string matchingName)
	{
		IEnumerable<XElement> source = from el in root.Elements("Item")
			where (string?)el.Attribute("name") == matchingName
			select el;
		if (source.Count() == 1)
		{
			if (double.TryParse(source.ElementAt(0).Value, out var result))
			{
				return result;
			}
			return double.NaN;
		}
		return double.NaN;
	}

	protected static string ParseGdalXMLString(XElement root, string matchingName)
	{
		IEnumerable<XElement> source = from el in root.Elements("Item")
			where (string?)el.Attribute("name") == matchingName
			select el;
		if (source.Count() == 0)
		{
			return "";
		}
		return Convert.ToString(source.ElementAt(0).Value);
	}

	public static void ReadGDALStats(string TiffFilename, int DirectoryNumber, out double Min, out double Max, out double Mean, out double StdDev, out double FractionNodata)
	{
		using FloatTiffReader floatTiffReader = new FloatTiffReader(TiffFilename);
		floatTiffReader.ReadGDALStats(DirectoryNumber, out Min, out Max, out Mean, out StdDev, out FractionNodata);
	}

	public void ReadGDALStats(int DirectoryNumber, out double Minimum, out double Maximum, out double Mean, out double StandardDeviation, out double FractionNodata)
	{
		Minimum = double.NaN;
		Maximum = double.NaN;
		Mean = double.NaN;
		StandardDeviation = double.NaN;
		FractionNodata = double.NaN;
		XElement gDALXML = GetGDALXML(DirectoryNumber);
		if (gDALXML != null)
		{
			Mean = ParseGdalXMLDouble(gDALXML, TiffBase.MEAN_NAME);
			Minimum = ParseGdalXMLDouble(gDALXML, TiffBase.MINIMUM_NAME);
			Maximum = ParseGdalXMLDouble(gDALXML, TiffBase.MAXIMUM_NAME);
			StandardDeviation = ParseGdalXMLDouble(gDALXML, TiffBase.STANDARD_DEVIATION_NAME);
			FractionNodata = ParseGdalXMLDouble(gDALXML, TiffBase.FRACTION_NODATA_NAME);
		}
	}

	public static int[] GetHistogram(string tiffFilename)
	{
		using FloatTiffReader floatTiffReader = new FloatTiffReader(tiffFilename);
		string[] parts = ParseGdalXMLString(floatTiffReader.GetGDALXML(0), TiffBase.HISTOGRAM_NAME).Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
		List<int> intList = new List<int>();
		for (int i = 0; i < parts.Length; i++)
		{
			intList.Add(Convert.ToInt32(parts[i]));
		}
		return intList.ToArray();
	}

	public void PrintDebugInfo(string FileNameOut)
	{
		File.Delete(FileNameOut);
		using StreamWriter streamWriter = new StreamWriter(FileNameOut);
		short numberOfDirectories = _tiffImage.NumberOfDirectories();
		for (short ordinal = 0; ordinal < numberOfDirectories; ordinal++)
		{
			if (ordinal != 0)
			{
				streamWriter.WriteLine("---------------------------------");
			}
			SetDirectory(ordinal);
			streamWriter.WriteLine("Image {0}, page {1} has following tags set:\n", _tiffFileName, ordinal);
			for (ushort uInt16 = 0; uInt16 < ushort.MaxValue; uInt16++)
			{
				TiffTag tag = (TiffTag)uInt16;
				FieldValue[] field = _tiffImage.GetField(tag);
				if (field != null)
				{
					for (int i = 0; i < field.Length; i++)
					{
						streamWriter.WriteLine("{0}", tag.ToString());
						streamWriter.WriteLine("{0} : {1}", field[i].Value.GetType().ToString(), field[i].ToString());
					}
					streamWriter.WriteLine();
				}
			}
		}
	}

	protected override void Dispose(bool isDisposing)
	{
		if (!_isDisposed)
		{
			base.Dispose(isDisposing);
			if (isDisposing)
			{
				_buffer = null;
			}
		}
	}
}
