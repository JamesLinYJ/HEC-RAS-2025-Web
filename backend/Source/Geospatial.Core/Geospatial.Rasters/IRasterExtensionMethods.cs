using System;
using System.Text;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public static class IRasterExtensionMethods
{
	public static T[] Read<T>(this IRaster<T> raster, Extent targetExtent, out RasterDefinition outRaster) where T : unmanaged, IEquatable<T>
	{
		outRaster = raster.RasterDefinition.GetRegisteredRaster(targetExtent);
		return raster.Resample(new ResampleArgs(outRaster));
	}

	public static T[] Read<T>(this IRaster<T> raster, PixelRange range) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition subRaster = raster.RasterDefinition.GetSubRaster(range);
		return raster.Resample(new ResampleArgs(subRaster));
	}

	public static T[] CopyData<T>(this IRaster<T> raster) where T : unmanaged, IEquatable<T>
	{
		return raster.Read(raster.RasterDefinition.FullRange());
	}

	public static RasterData<T> InMemory<T>(this IRaster<T> raster) where T : unmanaged, IEquatable<T>
	{
		return new RasterData<T>(raster.RasterDefinition, raster.CopyData());
	}

	public static string DebugDisplayNoData<T>(this IResample<T> resampler, int pixelCount) where T : unmanaged, IEquatable<T>
	{
		return resampler.AsRaster(RasterDefinition.ComputeBoundingRaster(resampler.ValidExtent, pixelCount)).DebugDisplayNoData();
	}

	public static string DebugDisplayNoData<T>(this IRaster<T> raster) where T : unmanaged, IEquatable<T>
	{
		if (!raster.NoDataValue.HasValue)
		{
			return "This raster doesn't have a NoData value.";
		}
		if (raster.RasterDefinition.CellCount > 10000)
		{
			return "Raster size exceeds maximum of " + 10000;
		}
		bool dEBUG_SERIALIZE_READS = Constants.DEBUG_SERIALIZE_READS;
		Constants.DEBUG_SERIALIZE_READS = true;
		RasterDefinition rasterDefinition = raster.RasterDefinition;
		StringBuilder stringBuilder = new StringBuilder();
		T noDataValueRasterTerm = raster.NoDataValue.Value;
		T[] copyData = raster.CopyData();
		for (int i = 0; i < rasterDefinition.Rows; i++)
		{
			StringBuilder stringBuilder2 = new StringBuilder();
			for (int j = 0; j < rasterDefinition.Cols; j++)
			{
				T copyData2 = copyData[rasterDefinition.CellIndex(i, j)];
				if (copyData2.Equals(noDataValueRasterTerm))
				{
					stringBuilder2.Append("--");
				}
				else
				{
					stringBuilder2.Append("oo");
				}
			}
			stringBuilder.AppendLine(stringBuilder2.ToString());
		}
		Constants.DEBUG_SERIALIZE_READS = dEBUG_SERIALIZE_READS;
		return stringBuilder.ToString();
	}

	public static string DebugDisplayRasterAsHistogram(this IResample<float> resampler, int nPixels = 1000)
	{
		if (nPixels > 10000)
		{
			return "Raster size exceeds maximum of " + 10000;
		}
		IRaster<float> raster = resampler.AsRaster(RasterDefinition.ComputeBoundingRaster(resampler.ValidExtent, nPixels));
		bool dEBUG_SERIALIZE_READS = Constants.DEBUG_SERIALIZE_READS;
		Constants.DEBUG_SERIALIZE_READS = true;
		RasterDefinition rasterDefinition = raster.RasterDefinition;
		StringBuilder stringBuilder = new StringBuilder();
		float naN = float.NaN;
		if (raster.NoDataValue.HasValue)
		{
			naN = raster.NoDataValue.Value;
		}
		float[] copyData = raster.CopyData();
		double[] quantiles = Histogram.ComputeFrom(copyData, 256, naN).ComputeQuantiles(5);
		for (int i = 0; i < rasterDefinition.Rows; i++)
		{
			StringBuilder stringBuilder2 = new StringBuilder();
			stringBuilder2.Append("|");
			for (int j = 0; j < rasterDefinition.Cols; j++)
			{
				float copyData2 = copyData[rasterDefinition.CellIndex(i, j)];
				if (copyData2.Equals(naN))
				{
					stringBuilder2.Append("  ");
					continue;
				}
				int unity = -1;
				for (int k = 0; k < quantiles.Length; k++)
				{
					if ((double)copyData2 <= quantiles[k])
					{
						unity = ((k != 0) ? (k - 1) : 0);
						break;
					}
				}
				if (unity == -1)
				{
					unity = quantiles.Length - 1;
				}
				switch (unity)
				{
				case 0:
					stringBuilder2.Append("__");
					break;
				case 1:
					stringBuilder2.Append("--");
					break;
				case 2:
					stringBuilder2.Append("oo");
					break;
				case 3:
					stringBuilder2.Append("//");
					break;
				default:
					stringBuilder2.Append("++");
					break;
				}
			}
			stringBuilder2.Append("|");
			stringBuilder.AppendLine(stringBuilder2.ToString());
		}
		Constants.DEBUG_SERIALIZE_READS = dEBUG_SERIALIZE_READS;
		return stringBuilder.ToString();
	}

	public static string DebugDisplayRasterExactValues(this IRaster<float> raster, int round = 2)
	{
		if (raster.RasterDefinition.CellCount > 100)
		{
			return "Raster size exceeds maximum of " + 100;
		}
		bool dEBUG_SERIALIZE_READS = Constants.DEBUG_SERIALIZE_READS;
		Constants.DEBUG_SERIALIZE_READS = true;
		RasterDefinition rasterDefinition = raster.RasterDefinition;
		StringBuilder stringBuilder = new StringBuilder();
		float[] copyData = raster.CopyData();
		int[] intValues = new int[rasterDefinition.Cols];
		for (int i = 0; i < rasterDefinition.Rows; i++)
		{
			for (int j = 0; j < rasterDefinition.Cols; j++)
			{
				string copyDataCellIndexTerm = Math.Round(copyData[rasterDefinition.CellIndex(i, j)], round).ToString();
				intValues[j] = Math.Max(copyDataCellIndexTerm.Length, intValues[j]);
			}
		}
		for (int k = 0; k < rasterDefinition.Rows; k++)
		{
			StringBuilder stringBuilder2 = new StringBuilder();
			for (int l = 0; l < rasterDefinition.Cols; l++)
			{
				int ordinal = intValues[l];
				string copyDataCellIndexTerm2 = Math.Round(copyData[rasterDefinition.CellIndex(k, l)], round).ToString();
				stringBuilder2.Append(copyDataCellIndexTerm2.PadRight(2 + ordinal));
			}
			stringBuilder.AppendLine(stringBuilder2.ToString());
		}
		Constants.DEBUG_SERIALIZE_READS = dEBUG_SERIALIZE_READS;
		return stringBuilder.ToString();
	}

	public static Histogram DebugComputeHistogram(this IResample<float> resampler, int pixelCount, int binCount)
	{
		bool dEBUG_SERIALIZE_READS = Constants.DEBUG_SERIALIZE_READS;
		Constants.DEBUG_SERIALIZE_READS = true;
		Histogram result = Histogram.ComputeTiled(resampler, pixelCount, binCount);
		Constants.DEBUG_SERIALIZE_READS = dEBUG_SERIALIZE_READS;
		return result;
	}

	public static Histogram DebugComputeHistogram(this IRaster<float> raster, int binCount)
	{
		bool dEBUG_SERIALIZE_READS = Constants.DEBUG_SERIALIZE_READS;
		Constants.DEBUG_SERIALIZE_READS = true;
		Histogram result = Histogram.ComputeFrom(raster, binCount);
		Constants.DEBUG_SERIALIZE_READS = dEBUG_SERIALIZE_READS;
		return result;
	}
}
