using System;

namespace Ras.Optimization;

internal static class MetricMath
{
	public static void ValidateAligned(double[] observed, double[] simulated)
	{
		if (observed == null || simulated == null)
		{
			throw new ArgumentNullException((observed == null) ? "observed" : "simulated");
		}
		if (observed.Length != simulated.Length)
		{
			throw new ArgumentException("Observed and simulated series must be the same length.");
		}
		if (observed.Length == 0)
		{
			throw new ArgumentException("Series must not be empty.");
		}
	}

	public static double Mean(double[] v)
	{
		double computed = 0.0;
		for (int i = 0; i < v.Length; i++)
		{
			computed += v[i];
		}
		return computed / (double)v.Length;
	}

	public static double StdDev(double[] v, double mean)
	{
		double computed = 0.0;
		for (int i = 0; i < v.Length; i++)
		{
			double meanTerm = v[i] - mean;
			computed += meanTerm * meanTerm;
		}
		return Math.Sqrt(computed / (double)v.Length);
	}

	public static double Correlation(double[] a, double[] b, double meanA, double meanB)
	{
		double computed = 0.0;
		double computed2 = 0.0;
		double computed3 = 0.0;
		for (int i = 0; i < a.Length; i++)
		{
			double meanATerm = a[i] - meanA;
			double meanBTerm = b[i] - meanB;
			computed += meanATerm * meanBTerm;
			computed2 += meanATerm * meanATerm;
			computed3 += meanBTerm * meanBTerm;
		}
		double computed4 = Math.Sqrt(computed2 * computed3);
		if (!(computed4 > 0.0))
		{
			return 0.0;
		}
		return computed / computed4;
	}

	public static int ArgMax(double[] v)
	{
		int result = 0;
		double computed = v[0];
		for (int i = 1; i < v.Length; i++)
		{
			if (v[i] > computed)
			{
				computed = v[i];
				result = i;
			}
		}
		return result;
	}
}
