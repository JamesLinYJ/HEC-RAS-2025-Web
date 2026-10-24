using System;

namespace Utility.Extensions;

public static class NumericExtensions
{
	public static int Bound(this int a, int lower, int upper)
	{
		if (a < lower)
		{
			return lower;
		}
		if (a > upper)
		{
			return upper;
		}
		return a;
	}

	public static double RoundToSignificantDigits(this double d, int digits)
	{
		if (d == 0.0)
		{
			return 0.0;
		}
		double computed = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Abs(d))) + 1.0);
		return computed * Math.Round(d / computed, digits);
	}
}
