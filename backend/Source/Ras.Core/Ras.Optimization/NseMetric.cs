namespace Ras.Optimization;

public sealed class NseMetric : IObjectiveMetric
{
	public string Name => "NSE";

	public double Evaluate(double[] timesSeconds, double[] observed, double[] simulated)
	{
		MetricMath.ValidateAligned(observed, simulated);
		double mean = MetricMath.Mean(observed);
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = 0; i < observed.Length; i++)
		{
			double observedSimulatedTerm = observed[i] - simulated[i];
			computed += observedSimulatedTerm * observedSimulatedTerm;
			double observedMeanTerm = observed[i] - mean;
			computed2 += observedMeanTerm * observedMeanTerm;
		}
		if (computed2 <= 0.0)
		{
			if (!(computed > 0.0))
			{
				return 0.0;
			}
			return double.PositiveInfinity;
		}
		double computed3 = 1.0 - computed / computed2;
		return 1.0 - computed3;
	}
}
