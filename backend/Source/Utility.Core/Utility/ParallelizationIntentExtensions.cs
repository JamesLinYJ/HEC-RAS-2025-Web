namespace Utility;

public static class ParallelizationIntentExtensions
{
	public static ParallelizationPlan WithItemThreshold(this ParallelizationIntent intent, int parallelItemThreshold)
	{
		return new ParallelizationPlan(intent, parallelItemThreshold);
	}
}
