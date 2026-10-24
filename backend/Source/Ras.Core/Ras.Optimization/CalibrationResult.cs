using System.Collections.Generic;

namespace Ras.Optimization;

public sealed class CalibrationResult
{
	public double[] BestPhysical { get; init; }

	public double BestObjective { get; init; }

	public IReadOnlyList<ICalibrationParameter> Parameters { get; init; }

	public GaugeMetricResult[] OptimumPerGauge { get; init; }

	public RunResult OptimumRun { get; init; }

	public IReadOnlyList<RunResult> History { get; init; }

	public double[] ConvergenceTrace { get; init; }

	public StopReason StopReason { get; init; }

	public int TotalRuns { get; init; }

	public string ConvergenceChartPath { get; init; }

	public IReadOnlyDictionary<string, double> BestByName()
	{
		Dictionary<string, double> doubleByString = new Dictionary<string, double>();
		for (int i = 0; i < Parameters.Count; i++)
		{
			doubleByString[Parameters[i].Name] = BestPhysical[i];
		}
		return doubleByString;
	}
}
