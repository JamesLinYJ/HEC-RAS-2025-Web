namespace Ras.Synthetics.SyntheticParams;

public readonly struct RiemannState(double depth, double velocity)
{
	public readonly double Depth = depth;

	public readonly double Velocity = velocity;
}
