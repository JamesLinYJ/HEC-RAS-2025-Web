using System.Numerics;

namespace Utility.NumericalAnalysis;

public readonly struct StabilityResult<T> where T : INumber<T>
{
	public StabilityState State { get; }

	public InstabilityKind Kind { get; }

	public double Time { get; }

	public T Value { get; }

	public int OscSteps { get; }

	public T Delta { get; }

	public double GrowthRatio { get; }

	public bool IsWarningOrWorse => State >= StabilityState.Warning;

	public bool IsError => State == StabilityState.Error;

	public StabilityResult(StabilityState state, InstabilityKind kind, double time, T localValue, int oscSteps, T delta, double growthRatio)
	{
		State = state;
		Kind = kind;
		Time = time;
		Value = localValue;
		OscSteps = oscSteps;
		Delta = delta;
		GrowthRatio = growthRatio;
	}

	public static StabilityResult<T> Unknown(double time, T unknownIds)
	{
		return new StabilityResult<T>(StabilityState.Unknown, InstabilityKind.None, time, unknownIds, 0, T.Zero, double.NaN);
	}
}
