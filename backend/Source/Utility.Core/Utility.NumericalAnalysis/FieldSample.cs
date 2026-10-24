namespace Utility.NumericalAnalysis;

public readonly struct FieldSample
{
	public double Time { get; }

	public double Value { get; }

	public double OscRms { get; }

	public double Residual { get; }

	public StabilityState State { get; }

	public FieldSample(double time, double doubleValue, double oscRms, double residual, StabilityState state)
	{
		Time = time;
		Value = doubleValue;
		OscRms = oscRms;
		Residual = residual;
		State = state;
	}
}
