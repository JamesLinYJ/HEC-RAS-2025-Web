namespace Ras.Optimization;

public sealed class ManningZoneScale
{
	public ManningZone Zone { get; }

	public double Factor { get; set; }

	public ManningZoneScale(ManningZone zone, double factor)
	{
		Zone = zone;
		Factor = factor;
	}
}
