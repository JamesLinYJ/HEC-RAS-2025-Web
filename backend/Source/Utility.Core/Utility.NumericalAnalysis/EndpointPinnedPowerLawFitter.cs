using System;

namespace Utility.NumericalAnalysis;

public static class EndpointPinnedPowerLawFitter
{
	public static bool TryFit(ReadOnlySpan<double> x, ReadOnlySpan<double> y, double x0, double weightPower, out EndpointPinnedPowerLawFit fit)
	{
		fit = new EndpointPinnedPowerLawFit(double.NaN, double.NaN, double.NaN);
		if (x.Length != y.Length)
		{
			throw new ArgumentException("The x and y spans must have equal lengths.");
		}
		if (!double.IsFinite(weightPower) || weightPower < 0.0)
		{
			throw new ArgumentOutOfRangeException("weightPower");
		}
		if (x.Length < 2)
		{
			return false;
		}
		for (int i = 0; i < x.Length; i++)
		{
			if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]) || y[i] < 0.0)
			{
				throw new ArgumentException("Coordinates must be finite and values finite and nonnegative.");
			}
			if (i > 0 && x[i] <= x[i - 1])
			{
				throw new ArgumentException("Coordinates must be strictly increasing.", "x");
			}
		}
		double x0Term = x[x.Length - 1] - x0;
		double computed = y[y.Length - 1];
		if (!double.IsFinite(x0Term) || !double.IsFinite(computed) || x0Term <= 0.0 || computed <= 0.0)
		{
			return false;
		}
		double[] doubleValues = new double[Math.Max(0, x.Length - 2)];
		double[] doubleValuesArray = new double[doubleValues.Length];
		double[] doubleValues2 = new double[doubleValues.Length];
		int count = 0;
		double computed2 = 0.0;
		double computed3 = 0.0;
		for (int j = 1; j < x.Length - 1; j++)
		{
			if (!(x[j] <= x0) && !(y[j] <= 0.0))
			{
				double x0Term2 = Math.Log((x[j] - x0) / x0Term);
				double computed4 = Math.Log(y[j] / computed);
				double weightPowerTerm = ((weightPower == 0.0) ? 1.0 : Math.Pow(y[j] / computed, weightPower));
				if (double.IsFinite(x0Term2) && double.IsFinite(computed4) && double.IsFinite(weightPowerTerm))
				{
					doubleValues[count] = x0Term2;
					doubleValuesArray[count] = computed4;
					doubleValues2[count] = weightPowerTerm;
					count++;
					computed2 += weightPowerTerm * x0Term2 * x0Term2;
					computed3 += weightPowerTerm * x0Term2 * computed4;
				}
			}
		}
		if (count == 0 || computed2 <= 0.0)
		{
			return false;
		}
		double computed5 = computed3 / computed2;
		double computed6 = computed / Math.Pow(x0Term, computed5);
		if (!double.IsFinite(computed5) || !double.IsFinite(computed6))
		{
			return false;
		}
		double computed7 = 0.0;
		double computed8 = 0.0;
		for (int k = 0; k < count; k++)
		{
			computed7 += doubleValues2[k];
			computed8 += doubleValues2[k] * doubleValuesArray[k];
		}
		double computed9 = ((computed7 > 0.0) ? (computed8 / computed7) : 0.0);
		double computed10 = 0.0;
		double computed11 = 0.0;
		for (int l = 0; l < count; l++)
		{
			double computed12 = doubleValuesArray[l] - computed5 * doubleValues[l];
			double computed13 = doubleValuesArray[l] - computed9;
			computed10 += doubleValues2[l] * computed13 * computed13;
			computed11 += doubleValues2[l] * computed12 * computed12;
		}
		double rSquared = ((computed10 > 0.0) ? (1.0 - computed11 / computed10) : 1.0);
		fit = new EndpointPinnedPowerLawFit(computed5, computed6, rSquared);
		return true;
	}
}
