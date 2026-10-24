using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BitMiracle.LibTiff.Classic;

namespace TiffAssist;

public class TiffWriterBase : TiffBase
{
	protected XElement GdalTag { get; private set; } = new XElement("GDALMetadata");

	public TiffWriterBase(string tiffFilename, string appendArg = "", bool deleteExistingFile = true)
		: base(tiffFilename, (File.Exists(tiffFilename) && !deleteExistingFile) ? ("a" + appendArg) : ("w" + appendArg), deleteExistingFile)
	{
	}

	public TiffWriterBase(Stream stream)
		: base(stream)
	{
	}

	protected void WriteTileInternal(int TileNumber, byte[] data)
	{
		_tiffImage.WriteEncodedTile(TileNumber, data, data.Length);
	}

	public byte[] GetRawTile(int writableTileNumber, byte[] data)
	{
		_tiffImage.WriteEncodedTile(writableTileNumber, data, data.Length);
		long rawTileSize = _tiffImage.RawTileSize(writableTileNumber);
		data = new byte[rawTileSize];
		_tiffImage.ReadRawTile(writableTileNumber, data, 0, (int)rawTileSize);
		return data;
	}

	public void WriteRawTile(int tileNumber, byte[] data)
	{
		_tiffImage.WriteRawTile(tileNumber, data, data.Length);
	}

	public int GetImageHeight()
	{
		return GetImageHeight(_currentDirectory);
	}

	public int GetImageWidth()
	{
		return GetImageWidth(_currentDirectory);
	}

	public int GetNumBitsPerSample()
	{
		return GetNumBitsPerSample(_currentDirectory);
	}

	public int GetNumSamplesPerPixel()
	{
		return GetNumSamplesPerPixel(_currentDirectory);
	}

	public int GetNumTiles()
	{
		return GetNumTiles(_currentDirectory);
	}

	public int GetNumTilesTall()
	{
		return GetNumTilesTall(_currentDirectory);
	}

	public int GetNumTilesWide()
	{
		return GetNumTilesWide(_currentDirectory);
	}

	public int GetTileHeight()
	{
		return GetTileHeight(_currentDirectory);
	}

	public int GetTileWidth()
	{
		return GetTileWidth(_currentDirectory);
	}

	public void GetValidRegion(int TileNumber, out int ValidWidth, out int ValidHeight)
	{
		GetValidRegion(_currentDirectory, TileNumber, out ValidWidth, out ValidHeight);
	}

	public void SetGDALTagRounding(string gdalTagRounding)
	{
		SetGDALTag(TiffBase.PRECISION_NAME, gdalTagRounding);
	}

	public void SetGDALTagQuantiles(string gdalTagQuantiles)
	{
		SetGDALTag(TiffBase.QUANTILES_NAME, gdalTagQuantiles);
	}

	public void SetGDALTagDataType(string gdalTagDataType)
	{
		SetGDALTag(TiffBase.TYPE_NAME, gdalTagDataType);
	}

	public void SetGDALTagDataUnits(string gdalTagDataUnits)
	{
		SetGDALTag(TiffBase.UNITS_NAME, gdalTagDataUnits);
	}

	public void SetGDALTagMin(string gdalTagMin)
	{
		SetGDALTag(TiffBase.MINIMUM_NAME, gdalTagMin);
	}

	public void SetGDALTagMax(string gdalTagMax)
	{
		SetGDALTag(TiffBase.MAXIMUM_NAME, gdalTagMax);
	}

	public void SetGDALTagFracNodata(string gdalTagFracNodata)
	{
		SetGDALTag(TiffBase.FRACTION_NODATA_NAME, gdalTagFracNodata);
	}

	public void SetGDALTagMean(string gdalTagMean)
	{
		SetGDALTag(TiffBase.MEAN_NAME, gdalTagMean);
	}

	public void SetGDALTagStdDev(string gdalTagStdDev)
	{
		SetGDALTag(TiffBase.STANDARD_DEVIATION_NAME, gdalTagStdDev);
	}

	public void SetGDALTagHisto(string gdalTagHisto)
	{
		SetGDALTag(TiffBase.HISTOGRAM_NAME, gdalTagHisto);
	}

	private void SetGDALTag(string name, string textValue)
	{
		ClearGDALXml(name);
		XElement xElement = new XElement("Item");
		xElement.SetAttributeValue("name", name);
		xElement.SetAttributeValue("sample", "0");
		xElement.Value = textValue;
		GdalTag.Add(xElement);
	}

	private void ClearGDALXml(string key)
	{
		IEnumerable<XElement> enumerable = from el in GdalTag.Elements("Item")
			where (string?)el.Attribute("name") == key
			select el;
		if (enumerable != null && enumerable.Count() > 0)
		{
			enumerable.Remove();
		}
	}

	public void SetGeoLocation(double minX, double maxY, double cellSize)
	{
		_tiffImage.SetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG, new double[3] { cellSize, cellSize, 0.0 });
		_tiffImage.SetField(TiffTag.GEOTIFF_MODELTIEPOINTTAG, new double[6] { 0.0, 0.0, 0.0, minX, maxY, 0.0 });
	}

	public static void SetCustomTag(string tiffFilename, int directoryNumber, CustomTags tag, object customTag)
	{
		using TiffWriterBase tiffWriterBase = new TiffWriterBase(tiffFilename, "", deleteExistingFile: false);
		tiffWriterBase.SetDirectory((short)directoryNumber);
		tiffWriterBase._tiffImage.SetField((TiffTag)tag, customTag);
	}

	public static string GetHDR(int NumCols, int NumRows, double XLLCorner, double YLLCorner, int CellSize, float NoData)
	{
		return "ncols\t" + NumCols + "\nnrows\t" + NumRows + "\nxllcorner\t" + XLLCorner + "\nyllcorner\t" + YLLCorner + "\ncellsize\t" + CellSize + "\nNODATA_value\t" + NoData + "\nbyteorder\tLSBFIRST";
	}
}
