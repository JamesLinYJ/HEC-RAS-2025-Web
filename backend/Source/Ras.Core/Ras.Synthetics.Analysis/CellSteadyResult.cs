namespace Ras.Synthetics.Analysis;

public readonly struct CellSteadyResult
{
	public SteadyState State { get; }

	public double OnsetTime { get; }

	public SteadyResult Depth { get; }

	public SteadyResult Velocity { get; }

	public CellSteadyResult(SteadyState state, double onsetTime, SteadyResult depth, SteadyResult velocity)
	{
		State = state;
		OnsetTime = onsetTime;
		Depth = depth;
		Velocity = velocity;
	}
}
