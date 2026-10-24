using System;

namespace Geospatial.Rasters.TileProcessing.Histogram;

public sealed class HistogramTileState
{
	public long[] Counts { get; }

	public long NoDataCount { get; set; }

	public float ObservedMin { get; set; } = float.PositiveInfinity;

	public float ObservedMax { get; set; } = float.NegativeInfinity;

	public HistogramTileState(int binCount)
	{
		if (binCount < 1)
		{
			throw new ArgumentOutOfRangeException("binCount");
		}
		Counts = new long[binCount];
	}
}
