using System;

namespace Utility.NumericalAnalysis;

public sealed class MinimumLinkResult<TFit>
{
	public int[] KnotIndexes { get; }

	public TFit[] SegmentFits { get; }

	public MinimumLinkResult(int[] knotIndexes, TFit[] segmentFits)
	{
		if (knotIndexes == null)
		{
			throw new ArgumentNullException("knotIndexes");
		}
		if (segmentFits == null)
		{
			throw new ArgumentNullException("segmentFits");
		}
		int lengthOffset = ((knotIndexes.Length != 0) ? (knotIndexes.Length - 1) : 0);
		if (segmentFits.Length != lengthOffset)
		{
			throw new ArgumentException("There must be one fit for each link between consecutive knots.", "segmentFits");
		}
		KnotIndexes = knotIndexes;
		SegmentFits = segmentFits;
	}
}
