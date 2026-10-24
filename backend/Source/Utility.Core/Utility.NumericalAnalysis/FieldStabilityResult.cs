namespace Utility.NumericalAnalysis;

public readonly struct FieldStabilityResult
{
	public StabilityState State { get; }

	public InstabilityKind Kind { get; }

	public double Time { get; }

	public int Element { get; }

	public double Value { get; }

	public double OscPower { get; }

	public double Residual { get; }

	public FieldStabilityResult(StabilityState state, InstabilityKind kind, double time, int element, double doubleValue, double oscPower, double residual)
	{
		State = state;
		Kind = kind;
		Time = time;
		Element = element;
		Value = doubleValue;
		OscPower = oscPower;
		Residual = residual;
	}

	public static FieldStabilityResult Unknown(double time)
	{
		return new FieldStabilityResult(StabilityState.Unknown, InstabilityKind.None, time, -1, 0.0, 0.0, 0.0);
	}
}
