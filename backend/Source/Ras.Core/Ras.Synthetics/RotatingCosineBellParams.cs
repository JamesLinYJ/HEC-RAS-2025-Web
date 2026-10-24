using System;

namespace Ras.Synthetics;

public sealed class RotatingCosineBellParams : RotatingTransportProfileParams
{
	public static RotatingCosineBellParams Default()
	{
		return new RotatingCosineBellParams();
	}

	protected override double ProfileConcentration(double normalizedRadius)
	{
		if (normalizedRadius >= 1.0)
		{
			return 0.0;
		}
		return 0.5 * (1.0 + Math.Cos(Math.PI * normalizedRadius));
	}
}
