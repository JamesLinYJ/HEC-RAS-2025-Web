namespace Ras.Optimization;

public sealed class GaugeMetricResult
{
	public string GaugeName { get; init; }

	public string MetricName { get; init; }

	public bool Extracted { get; init; }

	public double RawMetric { get; init; }

	public double Weight { get; init; }

	public double WeightedContribution { get; init; }
}
