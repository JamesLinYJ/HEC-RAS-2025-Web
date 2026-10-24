using System;

namespace Ras.Optimization;

public sealed class PeakErrorTimingMetric : IObjectiveMetric
{
	private readonly double _magnitudeWeight;

	private readonly double _timingWeight;

	public string Name => "PeakErrorTiming";

	public PeakErrorTimingMetric(double magnitudeWeight = 1.0, double timingWeight = 1.0)
	{
		if (magnitudeWeight < 0.0 || timingWeight < 0.0)
		{
			throw new ArgumentException("Weights must be non-negative.");
		}
		if (magnitudeWeight == 0.0 && timingWeight == 0.0)
		{
			throw new ArgumentException("At least one weight must be positive.");
		}
		_magnitudeWeight = magnitudeWeight;
		_timingWeight = timingWeight;
	}

	public double Evaluate(double[] timesSeconds, double[] observed, double[] simulated)
	{
		MetricMath.ValidateAligned(observed, simulated);
		if (timesSeconds == null || timesSeconds.Length != observed.Length)
		{
			throw new ArgumentException("Times array must align with the series.", "timesSeconds");
		}
		int argMax = MetricMath.ArgMax(observed);
		int argMax2 = MetricMath.ArgMax(simulated);
		double observed2 = observed[argMax];
		double simulated2 = simulated[argMax2];
		double absoluteSimulated2AboveObserved2 = ((observed2 != 0.0) ? (Math.Abs(simulated2 - observed2) / Math.Abs(observed2)) : Math.Abs(simulated2 - observed2));
		double timesSecondStep = timesSeconds[^1] - timesSeconds[0];
		double absoluteTimesSecondStepOverTimesSecondStep = ((timesSecondStep > 0.0) ? (Math.Abs(timesSeconds[argMax2] - timesSeconds[argMax]) / timesSecondStep) : 0.0);
		return _magnitudeWeight * absoluteSimulated2AboveObserved2 + _timingWeight * absoluteTimesSecondStepOverTimesSecondStep;
	}
}
