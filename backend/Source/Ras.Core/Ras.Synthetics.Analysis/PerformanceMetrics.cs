using System;
using System.Collections.Generic;

namespace Ras.Synthetics.Analysis;

public readonly struct PerformanceMetrics
{
	public double Min { get; }

	public double Max { get; }

	public double AbsMax { get; }

	public double RMS { get; }

	public double Mean { get; }

	public double StdDev { get; }

	public double Correlation { get; }

	public double Efficiency { get; }

	public double Agreement { get; }

	public int Count { get; }

	private PerformanceMetrics(double min, double max, double absMax, double rms, double mean, double stdDev, double corr, double nse, double ioa, int count)
	{
		Min = min;
		Max = max;
		AbsMax = absMax;
		RMS = rms;
		Mean = mean;
		StdDev = stdDev;
		Correlation = corr;
		Efficiency = nse;
		Agreement = ioa;
		Count = count;
	}

	public static PerformanceMetrics Compute(IEnumerable<double> errors)
	{
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		double absoluteError = 0.0;
		double errorSum = 0.0;
		double errorPolynomialSum = 0.0;
		int ordinal = 0;
		foreach (double error in errors)
		{
			if (error < positiveInfinity)
			{
				positiveInfinity = error;
			}
			if (error > negativeInfinity)
			{
				negativeInfinity = error;
			}
			double absoluteError2 = Math.Abs(error);
			if (absoluteError2 > absoluteError)
			{
				absoluteError = absoluteError2;
			}
			errorSum += error;
			errorPolynomialSum += error * error;
			ordinal++;
		}
		if (ordinal == 0)
		{
			return new PerformanceMetrics(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, double.NaN, double.NaN, double.NaN, 0);
		}
		double errorSumTerm = errorSum / (double)ordinal;
		double computed = Math.Sqrt(errorPolynomialSum / (double)ordinal);
		double stdDev = Math.Sqrt(Math.Max(0.0, computed * computed - errorSumTerm * errorSumTerm));
		return new PerformanceMetrics(positiveInfinity, negativeInfinity, absoluteError, computed, errorSumTerm, stdDev, double.NaN, double.NaN, double.NaN, ordinal);
	}

	public static PerformanceMetrics Compute(IReadOnlyList<double> computed, IReadOnlyList<double> reference)
	{
		int count = Math.Min(computed.Count, reference.Count);
		if (count == 0)
		{
			return new PerformanceMetrics(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, double.NaN, double.NaN, double.NaN, 0);
		}
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		double computed2 = 0.0;
		double computed3 = 0.0;
		double computed4 = 0.0;
		double referenceSum = 0.0;
		for (int i = 0; i < count; i++)
		{
			double computedReferenceTerm = computed[i] - reference[i];
			if (computedReferenceTerm < positiveInfinity)
			{
				positiveInfinity = computedReferenceTerm;
			}
			if (computedReferenceTerm > negativeInfinity)
			{
				negativeInfinity = computedReferenceTerm;
			}
			double computed5 = Math.Abs(computedReferenceTerm);
			if (computed5 > computed2)
			{
				computed2 = computed5;
			}
			computed3 += computedReferenceTerm;
			computed4 += computedReferenceTerm * computedReferenceTerm;
			referenceSum += reference[i];
		}
		double countTerm = computed3 / (double)count;
		double countTerm2 = Math.Sqrt(computed4 / (double)count);
		double stdDev = Math.Sqrt(Math.Max(0.0, countTerm2 * countTerm2 - countTerm * countTerm));
		double referenceSumOverCount = referenceSum / (double)count;
		double computed6 = 0.0;
		double computed7 = 0.0;
		for (int j = 0; j < count; j++)
		{
			double referenceTerm = reference[j] - referenceSumOverCount;
			computed6 += referenceTerm * referenceTerm;
			double computedTerm = Math.Abs(computed[j] - referenceSumOverCount) + Math.Abs(referenceTerm);
			computed7 += computedTerm * computedTerm;
		}
		double computed8 = computed4;
		double nse = ((computed6 == 0.0) ? double.NaN : (1.0 - computed8 / computed6));
		double ioa = ((computed7 == 0.0) ? double.NaN : (1.0 - computed8 / computed7));
		double corr = PearsonCorrelation(computed, reference);
		return new PerformanceMetrics(positiveInfinity, negativeInfinity, computed2, countTerm2, countTerm, stdDev, corr, nse, ioa, count);
	}

	public static PerformanceMetrics ComputeWeighted(IReadOnlyList<double> errors, IReadOnlyList<double> weights)
	{
		int count = Math.Min(errors.Count, weights.Count);
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		double absoluteError = 0.0;
		double weightSum = 0.0;
		double weightTimesErrorSum = 0.0;
		double computed = 0.0;
		for (int i = 0; i < count; i++)
		{
			double error = errors[i];
			if (error < positiveInfinity)
			{
				positiveInfinity = error;
			}
			if (error > negativeInfinity)
			{
				negativeInfinity = error;
			}
			double absoluteError2 = Math.Abs(error);
			if (absoluteError2 > absoluteError)
			{
				absoluteError = absoluteError2;
			}
			double weight = weights[i];
			weightSum += weight;
			weightTimesErrorSum += weight * error;
			computed += weight * error * error;
		}
		if (count == 0 || weightSum == 0.0)
		{
			return new PerformanceMetrics(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, double.NaN, double.NaN, double.NaN, count);
		}
		double weightTimesErrorSumOverWeightSum = weightTimesErrorSum / weightSum;
		double weightSumTerm = Math.Sqrt(computed / weightSum);
		double stdDev = Math.Sqrt(Math.Max(0.0, weightSumTerm * weightSumTerm - weightTimesErrorSumOverWeightSum * weightTimesErrorSumOverWeightSum));
		return new PerformanceMetrics(positiveInfinity, negativeInfinity, absoluteError, weightSumTerm, weightTimesErrorSumOverWeightSum, stdDev, double.NaN, double.NaN, double.NaN, count);
	}

	public static PerformanceMetrics ComputeWeighted(IReadOnlyList<double> computed, IReadOnlyList<double> reference, IReadOnlyList<double> weights)
	{
		int count = Math.Min(computed.Count, Math.Min(reference.Count, weights.Count));
		if (count == 0)
		{
			return new PerformanceMetrics(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, double.NaN, double.NaN, double.NaN, 0);
		}
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		double computed2 = 0.0;
		double weightSum = 0.0;
		double computed3 = 0.0;
		double computed4 = 0.0;
		double computed5 = 0.0;
		double computed6 = 0.0;
		for (int i = 0; i < count; i++)
		{
			double computedReferenceTerm = computed[i] - reference[i];
			if (computedReferenceTerm < positiveInfinity)
			{
				positiveInfinity = computedReferenceTerm;
			}
			if (computedReferenceTerm > negativeInfinity)
			{
				negativeInfinity = computedReferenceTerm;
			}
			double computed7 = Math.Abs(computedReferenceTerm);
			if (computed7 > computed2)
			{
				computed2 = computed7;
			}
			double weight = weights[i];
			weightSum += weight;
			computed3 += weight * computedReferenceTerm;
			computed4 += weight * computedReferenceTerm * computedReferenceTerm;
			computed5 += weight * reference[i];
			computed6 += weight * computed[i];
		}
		if (weightSum == 0.0)
		{
			return new PerformanceMetrics(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, double.NaN, double.NaN, double.NaN, count);
		}
		double weightSumTerm = computed3 / weightSum;
		double weightSumTerm2 = Math.Sqrt(computed4 / weightSum);
		double stdDev = Math.Sqrt(Math.Max(0.0, weightSumTerm2 * weightSumTerm2 - weightSumTerm * weightSumTerm));
		double weightSumTerm3 = computed5 / weightSum;
		double weightSumTerm4 = computed6 / weightSum;
		double computed8 = computed4;
		double computed9 = 0.0;
		double computed10 = 0.0;
		double computed11 = 0.0;
		double computed12 = 0.0;
		for (int j = 0; j < count; j++)
		{
			double weight2 = weights[j];
			double referenceTerm = reference[j] - weightSumTerm3;
			double computedTerm = computed[j] - weightSumTerm4;
			computed9 += weight2 * referenceTerm * referenceTerm;
			double computedTerm2 = Math.Abs(computed[j] - weightSumTerm3) + Math.Abs(referenceTerm);
			computed10 += weight2 * computedTerm2 * computedTerm2;
			computed11 += weight2 * computedTerm * referenceTerm;
			computed12 += weight2 * computedTerm * computedTerm;
		}
		double nse = ((computed9 == 0.0) ? double.NaN : (1.0 - computed8 / computed9));
		double ioa = ((computed10 == 0.0) ? double.NaN : (1.0 - computed8 / computed10));
		double computed13 = Math.Sqrt(computed12 * computed9);
		double corr = ((computed13 == 0.0) ? double.NaN : (computed11 / computed13));
		return new PerformanceMetrics(positiveInfinity, negativeInfinity, computed2, weightSumTerm2, weightSumTerm, stdDev, corr, nse, ioa, count);
	}

	public static double PearsonCorrelation(IReadOnlyList<double> a, IReadOnlyList<double> b)
	{
		int count = Math.Min(a.Count, b.Count);
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = 0; i < count; i++)
		{
			computed += a[i];
			computed2 += b[i];
		}
		computed /= (double)count;
		computed2 /= (double)count;
		double computed3 = 0.0;
		double computed4 = 0.0;
		double computed5 = 0.0;
		for (int j = 0; j < count; j++)
		{
			double computed6 = a[j] - computed;
			double computed7 = b[j] - computed2;
			computed3 += computed6 * computed6;
			computed4 += computed7 * computed7;
			computed5 += computed6 * computed7;
		}
		double computed8 = Math.Sqrt(computed3 * computed4);
		if (computed8 == 0.0)
		{
			return double.NaN;
		}
		return computed5 / computed8;
	}
}
