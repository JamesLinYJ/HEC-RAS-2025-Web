using System;

namespace Ras.Optimization;

public sealed class RBFSurrogateOptions
{
	public int MaxCandidates { get; init; } = 2000;

	public double UniformFraction { get; init; } = 0.25;

	public double InitialSigma { get; init; } = 0.2;

	public double MinSigma { get; init; } = 0.001;

	public double MaxSigma { get; init; } = 0.2;

	public int SuccessesToExpand { get; init; } = 3;

	public double[] WeightCycle { get; init; } = new double[4] { 0.3, 0.5, 0.8, 0.95 };

	public double DuplicateTolerance { get; init; } = 1E-06;

	public double Ridge { get; init; } = 1E-08;

	public bool CapFitValuesAtMedian { get; init; }

	public void Validate()
	{
		if (MaxCandidates < 1)
		{
			throw new ArgumentException("MaxCandidates must be at least 1.", "MaxCandidates");
		}
		if (UniformFraction < 0.0 || UniformFraction > 1.0)
		{
			throw new ArgumentException("UniformFraction must be in [0, 1].", "UniformFraction");
		}
		if (InitialSigma <= 0.0)
		{
			throw new ArgumentException("InitialSigma must be positive.", "InitialSigma");
		}
		if (MinSigma <= 0.0)
		{
			throw new ArgumentException("MinSigma must be positive.", "MinSigma");
		}
		if (MaxSigma < MinSigma)
		{
			throw new ArgumentException("MaxSigma must be >= MinSigma.", "MaxSigma");
		}
		if (SuccessesToExpand < 1)
		{
			throw new ArgumentException("SuccessesToExpand must be at least 1.", "SuccessesToExpand");
		}
		if (WeightCycle == null || WeightCycle.Length == 0)
		{
			throw new ArgumentException("Weight cycle must be non-empty.", "WeightCycle");
		}
		double[] weightCycle = WeightCycle;
		foreach (double weightCycle2 in weightCycle)
		{
			if (weightCycle2 < 0.0 || weightCycle2 > 1.0)
			{
				throw new ArgumentException("Weight cycle values must be in [0, 1].", "WeightCycle");
			}
		}
		if (DuplicateTolerance <= 0.0)
		{
			throw new ArgumentException("DuplicateTolerance must be positive.", "DuplicateTolerance");
		}
		if (Ridge < 0.0)
		{
			throw new ArgumentException("Ridge must be non-negative.", "Ridge");
		}
	}
}
