using System;

namespace Utility.NumericalAnalysis;

public static class CurveApproximationError
{
	public static double MaximumRelativeLinear(double[] sourceX, double[] sourceY, double[] approximationX, double[] approximationY, RelativeErrorPolicy<double> policy)
	{
		ValidateSource(sourceX, sourceY);
		ValidateSource(approximationX, approximationY);
		if (sourceX[0] < approximationX[0] || sourceX[^1] > approximationX[^1])
		{
			throw new ArgumentException("The approximation must cover the source coordinate range.", "approximationX");
		}
		double maximumMagnitude = MaximumMagnitude(sourceY);
		double errorPolicyTerm = 0.0;
		int i = 0;
		for (int j = 0; j < sourceX.Length; j++)
		{
			for (; i < approximationX.Length - 2 && sourceX[j] > approximationX[i + 1]; i++)
			{
			}
			double interpolationFraction = (sourceX[j] - approximationX[i]) / (approximationX[i + 1] - approximationX[i]);
			double actual = approximationY[i] + interpolationFraction * (approximationY[i + 1] - approximationY[i]);
			errorPolicyTerm = Math.Max(errorPolicyTerm, policy.Error(sourceY[j], actual, maximumMagnitude));
		}
		return errorPolicyTerm;
	}

	public static double MaximumRelativeEndpointPolynomial(double[] sourceX, double[] sourceY, int[] knotIndexes, EndpointPolynomialFit<double>[] segmentFits, RelativeErrorPolicy<double> policy)
	{
		ValidateSource(sourceX, sourceY);
		if (knotIndexes == null || segmentFits == null)
		{
			throw new ArgumentNullException("knotIndexes", "Knot indexes and fits cannot be null.");
		}
		if (knotIndexes.Length < 2 || segmentFits.Length != knotIndexes.Length - 1)
		{
			throw new ArgumentException("There must be one fit per link between at least two knots.");
		}
		if (knotIndexes[0] != 0 || knotIndexes[^1] != sourceX.Length - 1)
		{
			throw new ArgumentException("The knot path must preserve both source endpoints.", "knotIndexes");
		}
		double maximumMagnitude = MaximumMagnitude(sourceY);
		double errorPolicyTerm = 0.0;
		int i = 0;
		for (int j = 0; j < sourceX.Length; j++)
		{
			for (; i < segmentFits.Length - 1 && j > knotIndexes[i + 1]; i++)
			{
			}
			int knotIndex = knotIndexes[i];
			int knotIndex2 = knotIndexes[i + 1];
			double t = (sourceX[j] - sourceX[knotIndex]) / (sourceX[knotIndex2] - sourceX[knotIndex]);
			double actual = segmentFits[i].Evaluate(sourceY[knotIndex], sourceY[knotIndex2], t);
			errorPolicyTerm = Math.Max(errorPolicyTerm, policy.Error(sourceY[j], actual, maximumMagnitude));
		}
		return errorPolicyTerm;
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

	private static void ValidateSource(double[] x, double[] y)
	{
		if (x == null || y == null)
		{
			throw new ArgumentNullException("x", "Source arrays cannot be null.");
		}
		if (x.Length != y.Length || x.Length < 2)
		{
			throw new ArgumentException("Source arrays must have equal lengths of at least two.");
		}
		for (int i = 0; i < x.Length; i++)
		{
			if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]))
			{
				throw new ArgumentException("Source values must be finite.");
			}
			if (i > 0 && x[i] <= x[i - 1])
			{
				throw new ArgumentException("Source coordinates must be strictly increasing.", "x");
			}
		}
	}
}
