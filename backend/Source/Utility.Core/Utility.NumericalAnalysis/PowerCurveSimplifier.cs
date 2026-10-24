using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Utility.NumericalAnalysis;

public static class PowerCurveSimplifier
{
	public static FixedPowerMinimumLinkResult FindMinimumLinks(ReadOnlySpan<double> x, ReadOnlySpan<double> y, double exponent, RelativeErrorPolicy<double> errorPolicy, ReadOnlySpan<int> requiredKnotIndexes = default(ReadOnlySpan<int>))
	{
		ValidateCurve(x, y);
		if (!double.IsFinite(exponent) || exponent <= 0.0)
		{
			throw new ArgumentOutOfRangeException("exponent");
		}
		if (x.Length == 0)
		{
			return new FixedPowerMinimumLinkResult(Array.Empty<int>(), exponent, 0.0);
		}
		if (x.Length == 1)
		{
			return new FixedPowerMinimumLinkResult(new int[1], exponent, 0.0);
		}
		double y2 = 1.0 / exponent;
		double[] doubleValues = new double[y.Length];
		double[] doubleValuesArray = new double[y.Length];
		double[] doubleValues2 = new double[y.Length];
		double[] maximumAbsoluteErrors = errorPolicy.CreateMaximumAbsoluteErrors(y);
		for (int i = 0; i < y.Length; i++)
		{
			doubleValues[i] = Math.Pow(y[i], y2);
			doubleValuesArray[i] = Math.Pow(Math.Max(0.0, y[i] - maximumAbsoluteErrors[i]), y2);
			doubleValues2[i] = Math.Pow(y[i] + maximumAbsoluteErrors[i], y2);
		}
		int[] knotIndexes = MinimumLinkBoundedLinearSimplifier.FindKnotIndexes(x, doubleValues, doubleValuesArray, doubleValues2, requiredKnotIndexes);
		double maximumRelativeError = MaximumRelativeError(x, y, knotIndexes, exponent, errorPolicy);
		return new FixedPowerMinimumLinkResult(knotIndexes, exponent, maximumRelativeError);
	}

	public static GreedySegmentedPowerResult FindGreedySegmented(ReadOnlySpan<double> x, ReadOnlySpan<double> y, RelativeErrorPolicy<double> errorPolicy, double minimumExponent, double maximumExponent, double coarseStep, int refinementSteps = 20)
	{
		ValidateCurve(x, y);
		if (x.Length == 0)
		{
			return new GreedySegmentedPowerResult(Array.Empty<SegmentedPowerKnot>(), 0.0);
		}
		if (x.Length == 1)
		{
			return new GreedySegmentedPowerResult(new SegmentedPowerKnot[1]
			{
				new SegmentedPowerKnot(0, double.NaN)
			}, 0.0);
		}
		if (!double.IsFinite(minimumExponent) || minimumExponent <= 0.0 || !double.IsFinite(maximumExponent) || maximumExponent < minimumExponent)
		{
			throw new ArgumentOutOfRangeException("minimumExponent");
		}
		if (!double.IsFinite(coarseStep) || coarseStep <= 0.0)
		{
			throw new ArgumentOutOfRangeException("coarseStep");
		}
		if (refinementSteps < 1)
		{
			throw new ArgumentOutOfRangeException("refinementSteps");
		}
		double maximumMagnitude = MaximumMagnitude(y);
		int unity = 2;
		List<int> intList = new List<int>(unity);
		CollectionsMarshal.SetCount(intList, unity);
		Span<int> intListSpan = CollectionsMarshal.AsSpan(intList);
		int unity2 = 0;
		intListSpan[unity2] = 0;
		unity2++;
		intListSpan[unity2] = x.Length - 1;
		List<int> intListList = intList;
		unity2 = 1;
		List<double> doubleList = new List<double>(unity2);
		CollectionsMarshal.SetCount(doubleList, unity2);
		Span<double> doubleListSpan = CollectionsMarshal.AsSpan(doubleList);
		unity = 0;
		doubleListSpan[unity] = FitSegmentExponent(x, y, 0, x.Length - 1, errorPolicy, maximumMagnitude, minimumExponent, maximumExponent, coarseStep, refinementSteps, out var maximumError);
		List<double> doubleListList = doubleList;
		unity = 1;
		List<double> doubleList2 = new List<double>(unity);
		CollectionsMarshal.SetCount(doubleList2, unity);
		Span<double> doubleList3 = CollectionsMarshal.AsSpan(doubleList2);
		unity2 = 0;
		doubleList3[unity2] = maximumError;
		List<double> doubleList4 = doubleList2;
		while (true)
		{
			int ordinal = -1;
			double relativeTolerance = errorPolicy.RelativeTolerance;
			for (int i = 0; i < doubleList4.Count; i++)
			{
				if (!(doubleList4[i] <= relativeTolerance))
				{
					relativeTolerance = doubleList4[i];
					ordinal = i;
				}
			}
			if (ordinal < 0)
			{
				break;
			}
			int lo = intListList[ordinal];
			int hi = intListList[ordinal + 1];
			int worstInterior = FindWorstInterior(x, y, lo, hi, doubleListList[ordinal], errorPolicy, maximumMagnitude);
			if (worstInterior < 0)
			{
				doubleList4[ordinal] = 0.0;
				continue;
			}
			intListList.Insert(ordinal + 1, worstInterior);
			doubleListList[ordinal] = FitSegmentExponent(x, y, lo, worstInterior, errorPolicy, maximumMagnitude, minimumExponent, maximumExponent, coarseStep, refinementSteps, out var maximumError2);
			doubleList4[ordinal] = maximumError2;
			doubleListList.Insert(ordinal + 1, FitSegmentExponent(x, y, worstInterior, hi, errorPolicy, maximumMagnitude, minimumExponent, maximumExponent, coarseStep, refinementSteps, out var maximumError3));
			doubleList4.Insert(ordinal + 1, maximumError3);
		}
		SegmentedPowerKnot[] segmentedPowerKnots = new SegmentedPowerKnot[intListList.Count];
		for (int j = 0; j < intListList.Count; j++)
		{
			segmentedPowerKnots[j] = new SegmentedPowerKnot(intListList[j], (j < doubleListList.Count) ? doubleListList[j] : double.NaN);
		}
		double computed = 0.0;
		for (int k = 0; k < doubleList4.Count; k++)
		{
			computed = Math.Max(computed, doubleList4[k]);
		}
		return new GreedySegmentedPowerResult(segmentedPowerKnots, computed);
	}

	public static double Reconstruct(double target, ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<SegmentedPowerKnot> knots)
	{
		if (knots.Length < 2)
		{
			throw new ArgumentException("At least two segmented knots are required.", "knots");
		}
		int i;
		for (i = 0; i + 1 < knots.Length - 1 && target > x[knots[i + 1].Index]; i++)
		{
		}
		return Reconstruct(target, x, y, knots[i].Index, knots[i + 1].Index, knots[i].ExponentToNextKnot);
	}

	public static double Reconstruct(double target, ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<int> knotIndexes, double exponent)
	{
		if (knotIndexes.Length < 2)
		{
			throw new ArgumentException("At least two knot indexes are required.", "knotIndexes");
		}
		int i;
		for (i = 0; i + 1 < knotIndexes.Length - 1 && target > x[knotIndexes[i + 1]]; i++)
		{
		}
		return Reconstruct(target, x, y, knotIndexes[i], knotIndexes[i + 1], exponent);
	}

	private static double FitSegmentExponent(ReadOnlySpan<double> x, ReadOnlySpan<double> y, int lo, int hi, RelativeErrorPolicy<double> errorPolicy, double maximumMagnitude, double minimumExponent, double maximumExponent, double coarseStep, int refinementSteps, out double maximumError)
	{
		if (hi <= lo + 1)
		{
			maximumError = 0.0;
			return 1.0;
		}
		double unity = 1.0;
		double positiveInfinity = double.PositiveInfinity;
		for (double minimumExponent2 = minimumExponent; minimumExponent2 <= maximumExponent + 1E-12; minimumExponent2 += coarseStep)
		{
			double segmentMaximumRelativeError = SegmentMaximumRelativeError(x, y, lo, hi, minimumExponent2, errorPolicy, maximumMagnitude);
			if (!(segmentMaximumRelativeError >= positiveInfinity))
			{
				positiveInfinity = segmentMaximumRelativeError;
				unity = minimumExponent2;
			}
		}
		double minimumExponentCoarseStepTerm = Math.Max(minimumExponent, unity - coarseStep);
		double maximumExponentCoarseStepTerm = Math.Min(maximumExponent, unity + coarseStep);
		for (int i = 0; i <= refinementSteps; i++)
		{
			double refinementStepsTerm = minimumExponentCoarseStepTerm + (maximumExponentCoarseStepTerm - minimumExponentCoarseStepTerm) * (double)i / (double)refinementSteps;
			double segmentMaximumRelativeError2 = SegmentMaximumRelativeError(x, y, lo, hi, refinementStepsTerm, errorPolicy, maximumMagnitude);
			if (!(segmentMaximumRelativeError2 >= positiveInfinity))
			{
				positiveInfinity = segmentMaximumRelativeError2;
				unity = refinementStepsTerm;
			}
		}
		maximumError = positiveInfinity;
		return unity;
	}

	private static int FindWorstInterior(ReadOnlySpan<double> x, ReadOnlySpan<double> y, int lo, int hi, double exponent, RelativeErrorPolicy<double> errorPolicy, double maximumMagnitude)
	{
		int result = -1;
		double errorTerm = -1.0;
		for (int i = lo + 1; i < hi; i++)
		{
			double actual = Reconstruct(x[i], x, y, lo, hi, exponent);
			double error = errorPolicy.Error(y[i], actual, maximumMagnitude);
			if (!(error <= errorTerm))
			{
				errorTerm = error;
				result = i;
			}
		}
		return result;
	}

	private static double SegmentMaximumRelativeError(ReadOnlySpan<double> x, ReadOnlySpan<double> y, int lo, int hi, double exponent, RelativeErrorPolicy<double> errorPolicy, double maximumMagnitude)
	{
		double errorErrorPolicyTerm = 0.0;
		for (int i = lo + 1; i < hi; i++)
		{
			double actual = Reconstruct(x[i], x, y, lo, hi, exponent);
			errorErrorPolicyTerm = Math.Max(errorErrorPolicyTerm, errorPolicy.Error(y[i], actual, maximumMagnitude));
		}
		return errorErrorPolicyTerm;
	}

	private static double MaximumRelativeError(ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<int> knots, double exponent, RelativeErrorPolicy<double> errorPolicy)
	{
		double maximumMagnitude = MaximumMagnitude(y);
		double errorErrorPolicyTerm = 0.0;
		int i = 0;
		for (int j = 0; j < x.Length; j++)
		{
			for (; i + 1 < knots.Length - 1 && j > knots[i + 1]; i++)
			{
			}
			double actual = Reconstruct(x[j], x, y, knots[i], knots[i + 1], exponent);
			errorErrorPolicyTerm = Math.Max(errorErrorPolicyTerm, errorPolicy.Error(y[j], actual, maximumMagnitude));
		}
		return errorErrorPolicyTerm;
	}

	private static double Reconstruct(double target, ReadOnlySpan<double> x, ReadOnlySpan<double> y, int lo, int hi, double exponent)
	{
		double interpolationFraction = (target - x[lo]) / (x[hi] - x[lo]);
		double y2 = 1.0 / exponent;
		double loY2Term = Math.Pow(y[lo], y2);
		double hiY2Term = Math.Pow(y[hi], y2);
		return Math.Pow(Math.Max(0.0, loY2Term + interpolationFraction * (hiY2Term - loY2Term)), exponent);
	}

	private static double MaximumMagnitude(ReadOnlySpan<double> values)
	{
		double computed = 0.0;
		for (int i = 0; i < values.Length; i++)
		{
			computed = Math.Max(computed, Math.Abs(values[i]));
		}
		return computed;
	}

	private static void ValidateCurve(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
	{
		if (x.Length != y.Length)
		{
			throw new ArgumentException("The x and y spans must have equal lengths.");
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
	}
}
