namespace Utility;

public readonly struct ParallelizationPlan(ParallelizationIntent intent, int parallelItemThreshold = 1)
{
	public readonly ParallelizationIntent Intent = intent;

	public readonly int ParallelItemThreshold = parallelItemThreshold;

	public bool ShouldParallelize(int itemCount)
	{
		if (Intent == ParallelizationIntent.Parallel)
		{
			return itemCount >= ParallelItemThreshold;
		}
		return false;
	}

	public static implicit operator ParallelizationPlan(ParallelizationIntent intent)
	{
		return new ParallelizationPlan(intent);
	}
}
