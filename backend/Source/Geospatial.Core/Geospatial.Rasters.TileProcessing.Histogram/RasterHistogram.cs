using System;

namespace Geospatial.Rasters.TileProcessing.Histogram;

public sealed class RasterHistogram
{
	public float Min { get; }

	public float BinSize { get; }

	public long[] Counts { get; }

	public long NoDataCount { get; }

	public float ObservedMin { get; }

	public float ObservedMax { get; }

	public int BinCount => Counts.Length;

	public float Max => Min + BinSize * (float)BinCount;

	public long Total
	{
		get
		{
			long countSum = 0L;
			long[] counts = Counts;
			foreach (long count in counts)
			{
				countSum += count;
			}
			return countSum;
		}
	}

	public RasterHistogram(float min, float binSize, long[] counts, long noDataCount, float observedMin, float observedMax)
	{
		Min = min;
		BinSize = binSize;
		Counts = counts;
		NoDataCount = noDataCount;
		ObservedMin = observedMin;
		ObservedMax = observedMax;
	}

	public void BinRange(int binIndex, out float low, out float high)
	{
		if ((uint)binIndex >= (uint)BinCount)
		{
			throw new ArgumentOutOfRangeException("binIndex");
		}
		low = Min + BinSize * (float)binIndex;
		high = low + BinSize;
	}

	public double Fraction(int binIndex)
	{
		long total = Total;
		if (total == 0L)
		{
			return 0.0;
		}
		return (double)Counts[binIndex] / (double)total;
	}
}
