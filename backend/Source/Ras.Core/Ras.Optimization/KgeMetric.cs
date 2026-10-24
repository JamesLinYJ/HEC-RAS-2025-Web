using System;

namespace Ras.Optimization;

public sealed class KgeMetric : IObjectiveMetric
{
	public string Name => "KGE";

	public double Evaluate(double[] timesSeconds, double[] observed, double[] simulated)
	{
		MetricMath.ValidateAligned(observed, simulated);
		double mean = MetricMath.Mean(observed);
		double mean2 = MetricMath.Mean(simulated);
		double stdDev = MetricMath.StdDev(observed, mean);
		double stdDev2 = MetricMath.StdDev(simulated, mean2);
		if (stdDev <= 0.0 || mean == 0.0)
		{
			return double.PositiveInfinity;
		}
		double correlation = MetricMath.Correlation(observed, simulated, mean, mean2);
		double stdDev2OverStdDev = stdDev2 / stdDev;
		double mean2OverMean = mean2 / mean;
		double correlationTerm = 1.0 - Math.Sqrt((correlation - 1.0) * (correlation - 1.0) + (stdDev2OverStdDev - 1.0) * (stdDev2OverStdDev - 1.0) + (mean2OverMean - 1.0) * (mean2OverMean - 1.0));
		return 1.0 - correlationTerm;
	}
}
