using System;

namespace Ras.Optimization;

public sealed class StoppingCriteria
{
	public double? StepTolerance { get; init; }

	public int? MaxEvaluations { get; init; }

	public double? ObjectiveTolerance { get; init; }

	public int ObjectiveWindow { get; init; } = 5;

	public TimeSpan? MaxWallClock { get; init; }

	public bool HasAny
	{
		get
		{
			if (!StepTolerance.HasValue && !MaxEvaluations.HasValue && !ObjectiveTolerance.HasValue)
			{
				return MaxWallClock.HasValue;
			}
			return true;
		}
	}

	public void Validate()
	{
		if (MaxEvaluations.HasValue && MaxEvaluations.Value < 1)
		{
			throw new InvalidOperationException("MaxEvaluations must be at least 1.");
		}
		if (StepTolerance.HasValue && StepTolerance.Value <= 0.0)
		{
			throw new InvalidOperationException("StepTolerance must be positive.");
		}
		if (ObjectiveTolerance.HasValue && ObjectiveTolerance.Value <= 0.0)
		{
			throw new InvalidOperationException("ObjectiveTolerance must be positive.");
		}
		if (MaxWallClock.HasValue && MaxWallClock.Value <= TimeSpan.Zero)
		{
			throw new InvalidOperationException("MaxWallClock must be positive.");
		}
		if (ObjectiveWindow < 1)
		{
			throw new InvalidOperationException("ObjectiveWindow must be at least 1.");
		}
	}
}
