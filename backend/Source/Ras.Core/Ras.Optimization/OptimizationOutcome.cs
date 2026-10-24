namespace Ras.Optimization;

public sealed class OptimizationOutcome
{
	public double[] BestPhysical { get; init; }

	public double BestObjective { get; init; }

	public int Evaluations { get; init; }

	public int Iterations { get; init; }

	public StopReason StopReason { get; init; }
}
