using System.IO;

namespace TiffAssist;

public class FloatTiffReader : TiffReader<float>
{
	public static bool IsRASTiff(string filename)
	{
		if (Path.GetExtension(filename) != ".tif")
		{
			return false;
		}
		if (TiffBase.IsBigTiff(filename))
		{
			return false;
		}
		if (!TiffBase.IsIEEFPData(filename))
		{
			return false;
		}
		int[] histogram = TiffReader<float>.GetHistogram(filename);
		if (histogram == null || histogram.Length < 3)
		{
			return false;
		}
		TiffReader<float>.ReadGDALStats(filename, 0, out var Min, out var Max, out var Mean, out var StdDev, out var FractionNodata);
		if (double.IsNaN(Min) || double.IsNaN(Max) || double.IsNaN(Mean) || double.IsNaN(StdDev) || double.IsNaN(FractionNodata))
		{
			return false;
		}
		using (FloatTiffReader floatTiffReader = new FloatTiffReader(filename))
		{
			if (floatTiffReader.GetNumBitsPerSample(0) != 32)
			{
				return false;
			}
			if (!floatTiffReader.IsTiled(0))
			{
				return false;
			}
			if (floatTiffReader.GetTileHeight(0) != 256 || floatTiffReader.GetTileWidth(0) != 256)
			{
				return false;
			}
			int numDirectories = floatTiffReader.GetNumDirectories();
			if (floatTiffReader.GetNumTiles(numDirectories - 1) >= 10)
			{
				return false;
			}
		}
		return true;
	}

	public FloatTiffReader(string TiffFileName)
		: base(TiffFileName)
	{
	}
}
