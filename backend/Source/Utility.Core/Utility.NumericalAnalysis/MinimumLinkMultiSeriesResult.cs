using System;

namespace Utility.NumericalAnalysis;

public sealed class MinimumLinkMultiSeriesResult<TFit>
{
	public int[] KnotIndexes { get; }

	public TFit[][] SegmentFitsBySeries { get; }

	public MinimumLinkMultiSeriesResult(int[] knotIndexes, TFit[][] segmentFitsBySeries)
	{
		if (knotIndexes == null)
		{
			throw new ArgumentNullException("knotIndexes");
		}
		if (segmentFitsBySeries == null)
		{
			throw new ArgumentNullException("segmentFitsBySeries");
		}
		int lengthOffset = ((knotIndexes.Length != 0) ? (knotIndexes.Length - 1) : 0);
		for (int i = 0; i < segmentFitsBySeries.Length; i++)
		{
			if (segmentFitsBySeries[i] == null)
			{
				throw new ArgumentException("Segment-fit series cannot be null.", "segmentFitsBySeries");
			}
			if (segmentFitsBySeries[i].Length != lengthOffset)
			{
				throw new ArgumentException("Every series must have one fit for each shared link.", "segmentFitsBySeries");
			}
		}
		KnotIndexes = knotIndexes;
		SegmentFitsBySeries = segmentFitsBySeries;
	}
}
