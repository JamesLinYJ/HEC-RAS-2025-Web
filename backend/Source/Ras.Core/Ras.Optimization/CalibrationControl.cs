using System;

namespace Ras.Optimization;

public sealed class CalibrationControl
{
	public OptimizationMethod Method { get; init; }

	public BoundsMode BoundsMode { get; init; } = BoundsMode.Transform;

	public int LhsSampleCount { get; init; } = 12;

	public int LhsStartCount { get; init; } = 3;

	public double InitialStepFraction { get; init; } = 0.25;

	public StoppingCriteria Stopping { get; init; } = new StoppingCriteria
	{
		StepTolerance = 0.01,
		MaxEvaluations = 200,
		ObjectiveTolerance = 1E-05,
		MaxWallClock = TimeSpan.FromHours(8)
	};

	public int CoresPerRun { get; init; } = 1;

	public int MaxConcurrentRuns { get; init; } = 1;

	public int RngSeed { get; init; } = 12345;

	public RunPersistenceMode PersistenceMode { get; init; }

	public ResultRetention Retention { get; init; }

	public string OutputDirectory { get; init; }

	public bool Resume { get; init; }

	public void Validate()
	{
		if (CoresPerRun < 1)
		{
			throw new InvalidOperationException("CoresPerRun must be at least 1.");
		}
		if (MaxConcurrentRuns < 1)
		{
			throw new InvalidOperationException("MaxConcurrentRuns must be at least 1.");
		}
		int maxConcurrentRunsTimesCoresPerRun = MaxConcurrentRuns * CoresPerRun;
		if (maxConcurrentRunsTimesCoresPerRun > Environment.ProcessorCount)
		{
			throw new InvalidOperationException($"Core budget {MaxConcurrentRuns} concurrent runs x {CoresPerRun} cores = {maxConcurrentRunsTimesCoresPerRun} exceeds the {Environment.ProcessorCount} available processors.");
		}
		if (Stopping == null)
		{
			throw new InvalidOperationException("Stopping criteria are required.");
		}
		Stopping.Validate();
		if (LhsSampleCount < 1)
		{
			throw new InvalidOperationException("LhsSampleCount must be at least 1.");
		}
		if (LhsStartCount < 1 || LhsStartCount > LhsSampleCount)
		{
			throw new InvalidOperationException("LhsStartCount must be between 1 and LhsSampleCount.");
		}
		if (PersistenceMode == RunPersistenceMode.FullPipeline && string.IsNullOrEmpty(OutputDirectory))
		{
			throw new InvalidOperationException("OutputDirectory is required for FullPipeline persistence.");
		}
		if (Resume && string.IsNullOrEmpty(OutputDirectory))
		{
			throw new InvalidOperationException("Resume requires OutputDirectory (the journal lives there).");
		}
	}
}
