namespace Ras.Layers.Computes;

public readonly struct RunTimeEstimate
{
	public double TotalSeconds { get; init; }

	public double ComputationalSpeed { get; init; }

	public double LowSeconds { get; init; }

	public double HighSeconds { get; init; }

	public double InitSeconds { get; init; }

	public double StepSeconds { get; init; }

	public double OutputSeconds { get; init; }

	public long PredictedStepCount { get; init; }

	public bool UsedFallbackCoefficients { get; init; }
}
