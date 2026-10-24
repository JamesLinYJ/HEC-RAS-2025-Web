using System;

namespace Ras.Optimization;

public sealed class RmseMetric : IObjectiveMetric
{
	public string Name => "RMSE";

	public double Evaluate(double[] timesSeconds, double[] observed, double[] simulated)
	{
		MetricMath.ValidateAligned(observed, simulated);
		double computed = 0.0;
		for (int i = 0; i < observed.Length; i++)
		{
			double observedSimulatedTerm = observed[i] - simulated[i];
			computed += observedSimulatedTerm * observedSimulatedTerm;
		}
		return Math.Sqrt(computed / (double)observed.Length);
	}
}
