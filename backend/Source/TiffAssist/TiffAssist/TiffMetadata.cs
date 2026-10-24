using Geospatial.Rasters;

namespace TiffAssist;

public class TiffMetadata<T> where T : struct
{
	public int TileWidth = 256;

	public int TileHeight = 256;

	public string Artist = "";

	public string Software = "";

	public string DataType = "";

	public string DataUnits = "";

	public string ImageDescription = "";

	public bool IsLowResOverlay;

	public string CompressionUsed;

	public T NoDataValue;

	public string NoDataValueStringOverride;

	public float? HistogramBinSize;

	public RoundTo Rounding;

	public T? MaxReasonableValue;

	public T? MinReasonableValue;

	public TiffMetadata(T noDataValue, RoundTo rounding)
	{
		NoDataValue = noDataValue;
		Rounding = rounding;
	}
}
