namespace Ras.Optimization;

public sealed class RunResult
{
	public int RunId { get; init; }

	public double[] Physical { get; init; }

	public double Objective { get; init; }

	public GaugeMetricResult[] PerGauge { get; init; }

	public bool Succeeded { get; init; }

	public string FailureReason { get; init; }

	public string ResultDirectory { get; init; }

	public bool Preserved { get; init; }
}
