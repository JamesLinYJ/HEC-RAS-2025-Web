using System.Text;
using BitMiracle.LibTiff.Classic;

namespace TiffAssist;

public class TiffReaderBase : TiffBase
{
	public TiffReaderBase(string filename)
		: base(filename, "r", deleteExistingFile: false)
	{
	}

	protected TiffReaderBase(string filename, string mode, bool deleteExistingFile = true)
		: base(filename, mode, deleteExistingFile)
	{
	}

	public bool TryGetCellSize(int directory, out double cellSize)
	{
		if (!TryGetTag(directory, TiffTag.GEOTIFF_MODELPIXELSCALETAG, out double[] tagValue))
		{
			cellSize = 0.0;
			return false;
		}
		if (tagValue == null || tagValue.Length == 0)
		{
			cellSize = 0.0;
			return false;
		}
		cellSize = tagValue[0];
		return true;
	}

	public bool TryGetUpperLeftCorner(int directory, out double leftX, out double topY)
	{
		if (!TryGetTag(directory, TiffTag.GEOTIFF_MODELTIEPOINTTAG, out double[] tagValue))
		{
			leftX = 0.0;
			topY = 0.0;
			return false;
		}
		if (tagValue == null || tagValue.Length < 5)
		{
			leftX = 0.0;
			topY = 0.0;
			return false;
		}
		leftX = tagValue[3];
		topY = tagValue[4];
		return true;
	}

	public bool TryGetGeoLocation(int directory, out double minX, out double maxY, out double cellSize)
	{
		bool upperLeftCorner = TryGetUpperLeftCorner(directory, out minX, out maxY);
		bool cellSize2 = TryGetCellSize(directory, out cellSize);
		if (upperLeftCorner & cellSize2)
		{
			return true;
		}
		return false;
	}

	public bool TryGetTag(int directory, TiffTag tag, out double[] tagValue)
	{
		SetDirectory((short)directory);
		FieldValue[] field = _tiffImage.GetField(tag);
		try
		{
			if (field == null || field.Length == 0)
			{
				tagValue = null;
				return false;
			}
			tagValue = field[0].ToDoubleArray();
			if (tagValue == null && field.Length >= 2)
			{
				tagValue = field[1].ToDoubleArray();
			}
			return tagValue != null;
		}
		catch
		{
			tagValue = null;
			return false;
		}
	}

	public bool TryGetTag(int directory, TiffTag tag, out int[] tagValue)
	{
		SetDirectory((short)directory);
		FieldValue[] field = _tiffImage.GetField(tag);
		try
		{
			if (field == null || field.Length == 0)
			{
				tagValue = null;
				return false;
			}
			tagValue = field[0].ToIntArray();
			if (tagValue == null && field.Length >= 2)
			{
				tagValue = field[1].ToIntArray();
			}
			return tagValue != null;
		}
		catch
		{
			tagValue = null;
			return false;
		}
	}

	public bool TryGetTag(int directory, TiffTag tag, out float[] tagValue)
	{
		SetDirectory((short)directory);
		FieldValue[] field = _tiffImage.GetField(tag);
		try
		{
			if (field == null || field.Length == 0)
			{
				tagValue = null;
				return false;
			}
			tagValue = field[0].ToFloatArray();
			if (tagValue == null && field.Length >= 2)
			{
				tagValue = field[1].ToFloatArray();
			}
			return tagValue != null;
		}
		catch
		{
			tagValue = null;
			return false;
		}
	}

	public bool TryGetTag(int directory, TiffTag tag, out short[] tagValue)
	{
		SetDirectory((short)directory);
		FieldValue[] field = _tiffImage.GetField(tag);
		try
		{
			if (field == null || field.Length == 0)
			{
				tagValue = null;
				return false;
			}
			tagValue = field[0].ToShortArray();
			if (tagValue == null && field.Length >= 2)
			{
				tagValue = field[1].ToShortArray();
			}
			return tagValue != null;
		}
		catch
		{
			tagValue = null;
			return false;
		}
	}

	public bool TryGetTag(int directory, CustomTags tag, out byte[] tagValue)
	{
		return TryGetTag(directory, (TiffTag)tag, out tagValue);
	}

	public bool TryGetTag(int directory, TiffTag tag, out byte[] tagValue)
	{
		SetDirectory((short)directory);
		FieldValue[] field = _tiffImage.GetField(tag);
		try
		{
			if (field == null || field.Length == 0)
			{
				tagValue = null;
				return false;
			}
			tagValue = field[0].ToByteArray();
			if (tagValue == null && field.Length >= 2)
			{
				tagValue = field[1].ToByteArray();
			}
			return tagValue != null;
		}
		catch
		{
			tagValue = null;
			return false;
		}
	}

	public bool TryGetTag(int directory, CustomTags tag, out string tagValue)
	{
		return TryGetTag(directory, (TiffTag)tag, out tagValue);
	}

	public bool TryGetTag(int directory, TiffTag tag, out string tagValue)
	{
		if (!TryGetTag(directory, tag, out byte[] tagValue2))
		{
			tagValue = "";
			return false;
		}
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		tagValue = aSCIIEncoding.GetString(tagValue2);
		return !string.IsNullOrEmpty(tagValue);
	}

	public bool IsTiled(int DirectoryNumber)
	{
		SetDirectory((short)DirectoryNumber);
		return _tiffImage.IsTiled();
	}

	public new int GetImageHeight(int DirectoryNumber)
	{
		return base.GetImageHeight(DirectoryNumber);
	}

	public new int GetImageWidth(int DirectoryNumber)
	{
		return base.GetImageWidth(DirectoryNumber);
	}

	public new int GetNumBitsPerSample(int DirectoryNumber)
	{
		return base.GetNumBitsPerSample(DirectoryNumber);
	}

	public new int GetNumSamplesPerPixel(int DirectoryNumber)
	{
		return base.GetNumSamplesPerPixel(DirectoryNumber);
	}

	public new int GetNumTiles(int DirectoryNumber)
	{
		return base.GetNumTiles(DirectoryNumber);
	}

	public new int GetNumTilesTall(int DirectoryNumber)
	{
		return base.GetNumTilesTall(DirectoryNumber);
	}

	public new int GetNumTilesWide(int DirectoryNumber)
	{
		return base.GetNumTilesWide(DirectoryNumber);
	}

	public new int GetTileHeight(int DirectoryNumber)
	{
		return base.GetTileHeight(DirectoryNumber);
	}

	public new int GetTileWidth(int DirectoryNumber)
	{
		return base.GetTileWidth(DirectoryNumber);
	}

	public new void GetValidRegion(int DirectoryNumber, int TileNumber, out int ValidWidth, out int ValidHeight)
	{
		base.GetValidRegion(DirectoryNumber, TileNumber, out ValidWidth, out ValidHeight);
	}
}
