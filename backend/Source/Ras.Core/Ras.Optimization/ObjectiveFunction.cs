using System;
using System.Collections.Generic;
using Ras.Layers;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class ObjectiveFunction
{
	private readonly IReadOnlyList<Gauge> _gauges;

	private readonly ISimulatedSeriesExtractor _extractor;

	public IReadOnlyList<Gauge> Gauges => _gauges;

	public ObjectiveFunction(IReadOnlyList<Gauge> gauges, ISimulatedSeriesExtractor extractor)
	{
		if (gauges == null || gauges.Count == 0)
		{
			throw new ArgumentException("At least one gauge is required.", "gauges");
		}
		_gauges = gauges;
		_extractor = extractor ?? throw new ArgumentNullException("extractor");
	}

	public double Evaluate(Result result, out GaugeMetricResult[] perGauge, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		perGauge = new GaugeMetricResult[_gauges.Count];
		double weightedContributionSum = 0.0;
		double weightSum = 0.0;
		bool condition = false;
		for (int i = 0; i < _gauges.Count; i++)
		{
			Gauge gauge = _gauges[i];
			if (!_extractor.TryExtractSeries(gauge, result, out var timesSeconds, out var values, pr))
			{
				condition = true;
				perGauge[i] = new GaugeMetricResult
				{
					GaugeName = gauge.Name,
					MetricName = gauge.Metric.Name,
					Extracted = false,
					RawMetric = double.PositiveInfinity,
					Weight = gauge.Weight,
					WeightedContribution = double.PositiveInfinity
				};
				continue;
			}
			double rawMetric = EvaluateGauge(gauge, timesSeconds, values);
			if (!double.IsFinite(rawMetric))
			{
				condition = true;
			}
			double weightedContribution = gauge.Weight * rawMetric;
			weightedContributionSum += weightedContribution;
			weightSum += gauge.Weight;
			perGauge[i] = new GaugeMetricResult
			{
				GaugeName = gauge.Name,
				MetricName = gauge.Metric.Name,
				Extracted = true,
				RawMetric = rawMetric,
				Weight = gauge.Weight,
				WeightedContribution = weightedContribution
			};
		}
		if (condition || weightSum <= 0.0)
		{
			return double.PositiveInfinity;
		}
		return weightedContributionSum / weightSum;
	}

	public static double EvaluateGauge(Gauge gauge, double[] simTimes, double[] simValues)
	{
		double[] simulated = AlignToObservedTimes(gauge.Observed, simTimes, simValues);
		return gauge.Metric.Evaluate(gauge.Observed.TimesSeconds, gauge.Observed.Values, simulated);
	}

	public static double[] AlignToObservedTimes(ObservedSeries observed, double[] simTimes, double[] simValues)
	{
		if (simTimes == null || simValues == null)
		{
			throw new ArgumentNullException((simTimes == null) ? "simTimes" : "simValues");
		}
		if (simTimes.Length != simValues.Length)
		{
			throw new ArgumentException("Simulated times and values must be the same length.");
		}
		if (simTimes.Length == 0)
		{
			throw new ArgumentException("Simulated series must not be empty.");
		}
		double[] timesSeconds = observed.TimesSeconds;
		double[] doubleValues = new double[timesSeconds.Length];
		int i = 0;
		for (int j = 0; j < timesSeconds.Length; j++)
		{
			double timesSecond = timesSeconds[j];
			if (timesSecond <= simTimes[0])
			{
				doubleValues[j] = simValues[0];
				continue;
			}
			if (timesSecond >= simTimes[^1])
			{
				doubleValues[j] = simValues[^1];
				continue;
			}
			for (; i < simTimes.Length - 1 && simTimes[i + 1] < timesSecond; i++)
			{
			}
			double simTime = simTimes[i];
			double simTime2 = simTimes[i + 1];
			if (simTime2 <= simTime)
			{
				doubleValues[j] = simValues[i];
				continue;
			}
			double submergenceRatio = (timesSecond - simTime) / (simTime2 - simTime);
			doubleValues[j] = simValues[i] + submergenceRatio * (simValues[i + 1] - simValues[i]);
		}
		return doubleValues;
	}
}
