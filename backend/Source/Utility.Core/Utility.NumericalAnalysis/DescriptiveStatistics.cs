using System;

namespace Utility.NumericalAnalysis;

public static class DescriptiveStatistics
{
	public static double Median(ReadOnlySpan<double> values)
	{
		return Percentile(values, 0.5);
	}

	public static double Percentile(ReadOnlySpan<double> values, double fraction)
	{
		if (values.Length == 0)
		{
			return double.NaN;
		}
		double[] doubleValues = values.ToArray();
		Array.Sort(doubleValues);
		return PercentileOfSorted(doubleValues, fraction);
	}

	public static double PercentileOfSorted(ReadOnlySpan<double> sortedValues, double fraction)
	{
		if (sortedValues.Length == 0)
		{
			return double.NaN;
		}
		double fractionSortedValuesTerm = Math.Clamp(fraction, 0.0, 1.0) * (double)(sortedValues.Length - 1);
		int ordinal = (int)Math.Floor(fractionSortedValuesTerm);
		int ordinal2 = (int)Math.Ceiling(fractionSortedValuesTerm);
		if (ordinal == ordinal2)
		{
			return sortedValues[ordinal];
		}
		return sortedValues[ordinal] + (fractionSortedValuesTerm - (double)ordinal) * (sortedValues[ordinal2] - sortedValues[ordinal]);
	}

	public static double SampleStandardDeviation(ReadOnlySpan<double> values, double mean)
	{
		if (values.Length < 2)
		{
			return 0.0;
		}
		double computed = 0.0;
		for (int i = 0; i < values.Length; i++)
		{
			double meanTerm = values[i] - mean;
			computed += meanTerm * meanTerm;
		}
		return Math.Sqrt(computed / (double)(values.Length - 1));
	}
}
