namespace Ras.Synthetics;

public sealed class RotatingEllipseParams : RotatingTransportProfileParams
{
	public static RotatingEllipseParams Default()
	{
		return new RotatingEllipseParams();
	}

	protected override double ProfileConcentration(double normalizedRadius)
	{
		if (!(normalizedRadius < 1.0))
		{
			return 0.0;
		}
		return 1.0;
	}
}
