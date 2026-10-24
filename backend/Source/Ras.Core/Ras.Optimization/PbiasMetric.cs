using System;

namespace Ras.Optimization;

public sealed class PbiasMetric : IObjectiveMetric
{
	public string Name => "PBIAS";

	public double Evaluate(double[] timesSeconds, double[] observed, double[] simulated)
	{
		MetricMath.ValidateAligned(observed, simulated);
		double observedSum = 0.0;
		double computed = 0.0;
		for (int i = 0; i < observed.Length; i++)
		{
			observedSum += observed[i];
			computed += simulated[i] - observed[i];
		}
		if (observedSum == 0.0)
		{
			return double.PositiveInfinity;
		}
		return Math.Abs(100.0 * computed / observedSum);
	}
}
