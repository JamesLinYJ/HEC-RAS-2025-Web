using System;

namespace Utility.NumericalAnalysis;

public sealed class FixedPowerMinimumLinkResult
{
	public int[] KnotIndexes { get; }

	public double Exponent { get; }

	public double MaximumRelativeError { get; }

	public FixedPowerMinimumLinkResult(int[] knotIndexes, double exponent, double maximumRelativeError)
	{
		KnotIndexes = knotIndexes ?? throw new ArgumentNullException("knotIndexes");
		Exponent = exponent;
		MaximumRelativeError = maximumRelativeError;
	}
}
