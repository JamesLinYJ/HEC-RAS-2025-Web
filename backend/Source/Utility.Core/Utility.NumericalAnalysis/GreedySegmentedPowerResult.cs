using System;

namespace Utility.NumericalAnalysis;

public sealed class GreedySegmentedPowerResult
{
	public SegmentedPowerKnot[] Knots { get; }

	public double MaximumRelativeError { get; }

	public GreedySegmentedPowerResult(SegmentedPowerKnot[] knots, double maximumRelativeError)
	{
		Knots = knots ?? throw new ArgumentNullException("knots");
		MaximumRelativeError = maximumRelativeError;
	}
}
