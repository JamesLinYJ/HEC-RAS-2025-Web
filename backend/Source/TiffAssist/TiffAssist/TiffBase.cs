using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using BitMiracle.LibTiff.Classic;
using Geospatial.Rasters;

namespace TiffAssist;

public abstract class TiffBase : IDisposable
{
	protected const string StreamName = "InMemory";

	protected Tiff _tiffImage;

	protected string _tiffFileName;

	protected static CustomErrorHandler CEH;

	protected short _currentDirectory;

	public static readonly string MINIMUM_NAME;

	public static readonly string MAXIMUM_NAME;

	public static readonly string MEAN_NAME;

	public static readonly string STANDARD_DEVIATION_NAME;

	public static readonly string HISTOGRAM_NAME;

	public static readonly string PRECISION_NAME;

	public static readonly string QUANTILES_NAME;

	public static readonly string FRACTION_NODATA_NAME;

	public static readonly string UNITS_NAME;

	public static readonly string TYPE_NAME;

	protected static Tiff.TiffExtendProc _parentExtender;

	protected bool _isDisposed;

	public string Filename => _tiffFileName;

	static TiffBase()
	{
		CEH = null;
		MINIMUM_NAME = "STATISTICS_MINIMUM";
		MAXIMUM_NAME = "STATISTICS_MAXIMUM";
		MEAN_NAME = "STATISTICS_MEAN";
		STANDARD_DEVIATION_NAME = "STATISTICS_STDDEV";
		HISTOGRAM_NAME = "HISTOGRAM";
		PRECISION_NAME = "DECIMAL_PRECISION";
		QUANTILES_NAME = "QUANTILES";
		FRACTION_NODATA_NAME = "FRACTION_NODATA";
		UNITS_NAME = "UNITS";
		TYPE_NAME = "TYPE";
		_parentExtender = Tiff.SetTagExtender(TagExtender);
	}

	public static void TagExtender(Tiff tif)
	{
		TiffFieldInfo[] tiffFieldInfos = new TiffFieldInfo[4]
		{
			new TiffFieldInfo(TiffTag.GEOTIFF_MODELPIXELSCALETAG, 3, 3, TiffType.DOUBLE, 65, okToChange: true, passCount: false, "ModelPixelScaleTag"),
			new TiffFieldInfo(TiffTag.GEOTIFF_MODELTIEPOINTTAG, 6, 6, TiffType.DOUBLE, 65, okToChange: true, passCount: false, "ModelTiePointTag"),
			new TiffFieldInfo((TiffTag)42112, -1, -1, TiffType.ASCII, 65, okToChange: true, passCount: false, "GDALMetadata"),
			new TiffFieldInfo(TiffTag.GDAL_NODATA, -1, -1, TiffType.ASCII, 65, okToChange: true, passCount: false, "NoData")
		};
		tif.MergeFieldInfo(tiffFieldInfos, tiffFieldInfos.Length);
		_parentExtender?.Invoke(tif);
	}

	protected TiffBase(string filename, string mode, bool deleteExistingFile = true)
	{
		if (deleteExistingFile && File.Exists(filename))
		{
			File.Delete(filename);
		}
		string directoryName = Path.GetDirectoryName(filename);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		if (CEH == null)
		{
			CEH = new CustomErrorHandler();
			Tiff.SetErrorHandler(CEH);
		}
		_tiffFileName = filename;
		_tiffImage = TiffOpenCheckMode(filename, ref mode);
		_currentDirectory = -1;
		if (_tiffImage == null)
		{
			throw new Exception("Could not open " + filename);
		}
	}

	protected static Tiff TiffOpenCheckMode(string filename, ref string mode)
	{
		if (File.Exists(filename))
		{
			if (IsBigTiff(filename) && mode.Contains('a') && !mode.Contains('8'))
			{
				mode += "8";
			}
			else if (mode.Contains('w'))
			{
				File.Delete(filename);
			}
		}
		return Tiff.Open(filename, mode);
	}

	protected TiffBase(Stream stream)
	{
		if (CEH == null)
		{
			CEH = new CustomErrorHandler();
			Tiff.SetErrorHandler(CEH);
		}
		_tiffFileName = "InMemory";
		_tiffImage = Tiff.ClientOpen("InMemory", "w", stream, new TiffStream());
		_currentDirectory = -1;
		if (_tiffImage == null)
		{
			throw new Exception("Could not open source stream.");
		}
	}

	public static string GetDescription(RoundTo Rounding)
	{
		DescriptionAttribute[] customAttributes = (DescriptionAttribute[])Rounding.GetType().GetField(Rounding.ToString()).GetCustomAttributes(typeof(DescriptionAttribute), inherit: false);
		if (customAttributes.Length == 0)
		{
			return Rounding.ToString();
		}
		return customAttributes[0].Description;
	}

	public static bool IsIEEFPData(string Filename)
	{
		using TiffReader<byte> tiffReader = new TiffReader<byte>(Filename);
		FieldValue[] field = tiffReader._tiffImage.GetField(TiffTag.SAMPLEFORMAT);
		try
		{
			if (field == null || field.Length == 0)
			{
				return false;
			}
			if ((SampleFormat)field[0].Value == SampleFormat.IEEEFP)
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

	internal static SampleFormat GetFormat(string filename)
	{
		using TiffReader<byte> tiffReader = new TiffReader<byte>(filename);
		return tiffReader.GetFormat();
	}

	public SampleFormat GetFormat()
	{
		FieldValue[] field = _tiffImage.GetField(TiffTag.SAMPLEFORMAT);
		try
		{
			if (field == null || field.Length == 0)
			{
				return SampleFormat.VOID;
			}
			return (SampleFormat)field[0].Value;
		}
		catch
		{
			return SampleFormat.VOID;
		}
	}

	public static int BitsPerSample(string filename)
	{
		using TiffReader<byte> tiffReader = new TiffReader<byte>(filename);
		return tiffReader.BitsPerSample();
	}

	public int BitsPerSample()
	{
		FieldValue[] field = _tiffImage.GetField(TiffTag.BITSPERSAMPLE);
		try
		{
			if (field == null || field.Length == 0)
			{
				return 0;
			}
			return (short)field[0].Value;
		}
		catch
		{
			return 0;
		}
	}

	public static bool IsBigTiff(string filename)
	{
		using StreamReader streamReader = new StreamReader(filename, Encoding.UTF8);
		bool condition = false;
		switch (streamReader.Read())
		{
		default:
			throw new Exception("Bad tiff format.");
		case 77:
			condition = true;
			break;
		case 73:
			break;
		}
		int read = streamReader.Read();
		if ((!condition && read != 73) || (condition && read != 77))
		{
			throw new Exception("Bad tiff format.");
		}
		read = streamReader.Read();
		if (condition)
		{
			read = streamReader.Read();
		}
		return read switch
		{
			43 => true, 
			42 => false, 
			_ => throw new Exception("Bad tiff format."), 
		};
	}

	public static bool IsTiffFile(string filename)
	{
		using StreamReader streamReader = new StreamReader(filename, Encoding.UTF8);
		bool condition = false;
		switch (streamReader.Read())
		{
		default:
			return false;
		case 77:
			condition = true;
			break;
		case 73:
			break;
		}
		int read = streamReader.Read();
		if ((!condition && read != 73) || (condition && read != 77))
		{
			return false;
		}
		read = streamReader.Read();
		if (condition)
		{
			read = streamReader.Read();
		}
		return read switch
		{
			43 => true, 
			42 => true, 
			_ => false, 
		};
	}

	protected void SetDirectory(short directoryNumber)
	{
		if (_currentDirectory != directoryNumber)
		{
			_tiffImage.SetDirectory(directoryNumber);
			_currentDirectory = directoryNumber;
		}
	}

	protected void SetDirectory(int directoryNumber)
	{
		SetDirectory((short)directoryNumber);
	}

	public int GetNumDirectories()
	{
		return _tiffImage.NumberOfDirectories();
	}

	protected int GetNumSamplesPerPixel(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
	}

	protected int GetNumBitsPerSample(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt();
	}

	protected int GetNumTilesWide(int DirectoryNumber)
	{
		int imageWidth = GetImageWidth(DirectoryNumber);
		int tileWidth = GetTileWidth(DirectoryNumber);
		return GetNumTilesWide(imageWidth, tileWidth);
	}

	protected static int GetNumTilesWide(int imgWidth, int tileWidth)
	{
		return Convert.ToInt32(Math.Ceiling((double)imgWidth / (double)tileWidth));
	}

	protected int GetNumTilesTall(int DirectoryNumber)
	{
		int imageHeight = GetImageHeight(DirectoryNumber);
		int tileHeight = GetTileHeight(DirectoryNumber);
		return Convert.ToInt32(Math.Ceiling((double)imageHeight / (double)tileHeight));
	}

	protected int GetNumTiles(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.NumberOfTiles();
	}

	protected int GetImageWidth(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
	}

	protected int GetImageHeight(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
	}

	protected int GetTileWidth(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.GetField(TiffTag.TILEWIDTH)[0].ToInt();
	}

	protected int GetTileHeight(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.GetField(TiffTag.TILELENGTH)[0].ToInt();
	}

	protected int GetNumValidCells(int DirectoryNumber, int tileIdx)
	{
		GetValidRegion(DirectoryNumber, tileIdx, out var validWidth, out var validHeight);
		return validWidth * validHeight;
	}

	public void GetValidRegion(int directoryNumber, int tileNumber, out int validWidth, out int validHeight)
	{
		int tileWidth = GetTileWidth(directoryNumber);
		int tileHeight = GetTileHeight(directoryNumber);
		int imageWidth = GetImageWidth(directoryNumber);
		int imageHeight = GetImageHeight(directoryNumber);
		GetValidRegionThreadsafe(tileWidth, tileHeight, imageWidth, imageHeight, tileNumber, out validWidth, out validHeight);
	}

	public static void GetValidRegionThreadsafe(int tileWidth, int tileHeight, int imgWidth, int imgHeight, int tileNumber, out int validWidth, out int validHeight)
	{
		int numTilesWide = GetNumTilesWide(imgWidth, tileWidth);
		int tileNumberWithNumTilesWide = tileNumber % numTilesWide;
		int tileNumberOverNumTilesWide = tileNumber / numTilesWide;
		int tileWidthImgWidthTerm = (tileNumberWithNumTilesWide + 1) * tileWidth - imgWidth;
		int tileHeightImgHeightTerm = (tileNumberOverNumTilesWide + 1) * tileHeight - imgHeight;
		if (tileWidthImgWidthTerm > 0)
		{
			validWidth = tileWidth - tileWidthImgWidthTerm;
		}
		else
		{
			validWidth = tileWidth;
		}
		if (tileHeightImgHeightTerm > 0)
		{
			validHeight = tileHeight - tileHeightImgHeightTerm;
		}
		else
		{
			validHeight = tileHeight;
		}
	}

	public Tuple<byte, byte, byte>[] GetColorMap(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		FieldValue[] field = _tiffImage.GetField(TiffTag.COLORMAP);
		if (field == null || field.Length != 3)
		{
			return null;
		}
		if (!(field[0].Value is short[] shortValues))
		{
			return null;
		}
		if (!(field[1].Value is short[] shortValuesArray))
		{
			return null;
		}
		if (!(field[2].Value is short[] shortValues2))
		{
			return null;
		}
		if (shortValues.Length != shortValues2.Length || shortValues.Length != shortValuesArray.Length)
		{
			return null;
		}
		float computed = 65535f;
		int count = shortValues.Length;
		Tuple<byte, byte, byte>[] tupleArray = new Tuple<byte, byte, byte>[count];
		for (int i = 0; i < count; i++)
		{
			byte localValue = (byte)Math.Round((float)(255 * (ushort)shortValues[i]) / computed);
			byte localValue2 = (byte)Math.Round((float)(255 * (ushort)shortValuesArray[i]) / computed);
			byte localValue3 = (byte)Math.Round((float)(255 * (ushort)shortValues2[i]) / computed);
			tupleArray[i] = new Tuple<byte, byte, byte>(localValue, localValue2, localValue3);
		}
		return tupleArray;
	}

	public static void PrintDebugInfo(string tiffFilename, string debugInfoFilename)
	{
		using TiffReader<byte> tiffReader = new TiffReader<byte>(tiffFilename);
		tiffReader.PrintDebugInfo(debugInfoFilename);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_tiffImage?.Dispose();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	~TiffBase()
	{
		Dispose(disposing: false);
	}
}
