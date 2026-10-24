using System.Numerics;

namespace Utility.NumericalAnalysis;

public readonly struct AggregateStabilityResult<T> where T : INumber<T>
{
	public StabilityState State { get; }

	public InstabilityKind Kind { get; }

	public double Time { get; }

	public int WorstElement { get; }

	public T WorstValue { get; }

	public T MaxDelta { get; }

	public int MaxOscSteps { get; }

	public int OscillatingElements { get; }

	public AggregateStabilityResult(StabilityState state, InstabilityKind kind, double time, int worstElement, T worstValue, T maxDelta, int maxOscSteps, int oscillatingElements)
	{
		State = state;
		Kind = kind;
		Time = time;
		WorstElement = worstElement;
		WorstValue = worstValue;
		MaxDelta = maxDelta;
		MaxOscSteps = maxOscSteps;
		OscillatingElements = oscillatingElements;
	}
}
