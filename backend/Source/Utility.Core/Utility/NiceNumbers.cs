using System;

namespace Utility;

public static class NiceNumbers
{
	public static double Ceiling125(double target)
	{
		if (!(target > 0.0) || double.IsInfinity(target))
		{
			return 1.0;
		}
		double decompose = Decompose(target, out var pow);
		double unity;
		if (decompose <= 1.0)
		{
			unity = 1.0;
		}
		else if (decompose <= 2.0)
		{
			unity = 2.0;
		}
		else
		{
			unity = ((!(decompose <= 5.0)) ? 10.0 : 5.0);
		}
		return unity * pow;
	}

	public static double Floor125(double target)
	{
		if (!(target > 0.0) || double.IsInfinity(target))
		{
			return 1.0;
		}
		double decompose = Decompose(target, out var pow);
		double computed;
		if (decompose >= 5.0)
		{
			computed = 5.0;
		}
		else
		{
			computed = ((!(decompose >= 2.0)) ? 1.0 : 2.0);
		}
		return computed * pow;
	}

	public static double Nearest125(double target)
	{
		if (!(target > 0.0) || double.IsInfinity(target))
		{
			return 1.0;
		}
		double decompose = Decompose(target, out var pow);
		double unity;
		if (decompose < 1.5)
		{
			unity = 1.0;
		}
		else if (decompose < 3.5)
		{
			unity = 2.0;
		}
		else
		{
			unity = ((!(decompose < 7.5)) ? 10.0 : 5.0);
		}
		return unity * pow;
	}

	private static double Decompose(double computed, out double pow10)
	{
		pow10 = Math.Pow(10.0, Math.Floor(Math.Log10(computed)));
		return computed / pow10;
	}
}
