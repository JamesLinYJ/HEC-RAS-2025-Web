namespace Ras.Synthetics.Analysis;

public readonly struct SteadyResult
{
	public SteadyState State { get; }

	public bool ReachedSteady { get; }

	public double OnsetTime { get; }

	public double SteadyValue { get; }

	public double FinalBand { get; }

	public double FinalDrift { get; }

	public SteadyResult(SteadyState state, bool reachedSteady, double onsetTime, double steadyValue, double finalBand, double finalDrift)
	{
		State = state;
		ReachedSteady = reachedSteady;
		OnsetTime = onsetTime;
		SteadyValue = steadyValue;
		FinalBand = finalBand;
		FinalDrift = finalDrift;
	}

	public static SteadyResult None()
	{
		return new SteadyResult(SteadyState.Unknown, reachedSteady: false, double.NaN, double.NaN, double.NaN, double.NaN);
	}
}
