using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Utility.NumericalAnalysis;

public sealed class EndpointPolynomialLinkCatalog
{
	private readonly EndpointPolynomialFit<double>[][] _fits;

	private readonly double[][] _maximumNormalizedErrors;

	public int PointCount { get; }

	public EndpointPolynomialOrder Order { get; }

	public double PreparationMilliseconds { get; }

	private EndpointPolynomialLinkCatalog(int pointCount, EndpointPolynomialOrder order, EndpointPolynomialFit<double>[][] fits, double[][] maximumNormalizedErrors, double preparationMilliseconds)
	{
		PointCount = pointCount;
		Order = order;
		_fits = fits;
		_maximumNormalizedErrors = maximumNormalizedErrors;
		PreparationMilliseconds = preparationMilliseconds;
	}

	public EndpointPolynomialFit<double> GetFit(int startIndex, int endIndex)
	{
		return _fits[startIndex][endIndex - startIndex - 1];
	}

	public double GetMaximumNormalizedError(int startIndex, int endIndex)
	{
		return _maximumNormalizedErrors[startIndex][endIndex - startIndex - 1];
	}

	public static EndpointPolynomialLinkCatalog Prepare(double[] x, double[] y, double[] errorDenominators, EndpointPolynomialOrder order, int[] requiredKnotIndexes, double rejectionThreshold, bool parallel = true)
	{
		ValidateInputs(x, y, errorDenominators, requiredKnotIndexes, rejectionThreshold);
		int count = x.Length;
		EndpointPolynomialFit<double>[][] fits = new EndpointPolynomialFit<double>[count][];
		double[][] errors = new double[count][];
		for (int i = 0; i < count; i++)
		{
			fits[i] = Array.Empty<EndpointPolynomialFit<double>>();
			errors[i] = Array.Empty<double>();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		int requiredKnotIndex = 0;
		for (int j = 0; j <= requiredKnotIndexes.Length; j++)
		{
			int requiredKnotIndex2 = ((j < requiredKnotIndexes.Length) ? requiredKnotIndexes[j] : (count - 1));
			if (requiredKnotIndex2 <= requiredKnotIndex)
			{
				continue;
			}
			int requiredKnotIndex3 = requiredKnotIndex;
			int localEnd = requiredKnotIndex2;
			if (parallel)
			{
				System.Threading.Tasks.Parallel.For(requiredKnotIndex3, localEnd, (int start) =>
				{
					PrepareStart(x, y, errorDenominators, order, rejectionThreshold, start, localEnd, fits, errors);
				});
			}
			else
			{
				for (int requiredKnotIndex32 = requiredKnotIndex3; requiredKnotIndex32 < localEnd; requiredKnotIndex32++)
				{
					PrepareStart(x, y, errorDenominators, order, rejectionThreshold, requiredKnotIndex32, localEnd, fits, errors);
				}
			}
			requiredKnotIndex = requiredKnotIndex2;
		}
		stopwatch.Stop();
		return new EndpointPolynomialLinkCatalog(count, order, fits, errors, stopwatch.Elapsed.TotalMilliseconds);
	}

	private static void PrepareStart(double[] x, double[] y, double[] denominators, EndpointPolynomialOrder order, double rejectionThreshold, int start, int partitionEnd, EndpointPolynomialFit<double>[][] fits, double[][] errors)
	{
		int count = partitionEnd - start;
		fits[start] = new EndpointPolynomialFit<double>[count];
		errors[start] = new double[count];
		Array.Fill(errors[start], double.PositiveInfinity);
		double[] rawPowers = new double[7];
		double[] rawDeltaYPowers = new double[4];
		double[] normalizedPowers = new double[7];
		double[] normalizedDeltaYPowers = new double[4];
		for (int i = start + 1; i <= partitionEnd; i++)
		{
			if (i > start + 1)
			{
				AddMoment(x, y, start, i - 1, rawPowers, rawDeltaYPowers);
			}
			if (TryFitFromMoments(x, y, start, i, order, rawPowers, rawDeltaYPowers, normalizedPowers, normalizedDeltaYPowers, out var fit))
			{
				int startTerm = i - start - 1;
				fits[start][startTerm] = fit;
				errors[start][startTerm] = MaximumSegmentNormalizedError(x, y, denominators, start, i, fit, rejectionThreshold);
			}
		}
	}

	private static void AddMoment(double[] x, double[] y, int start, int index, double[] rawPowers, double[] rawDeltaYPowers)
	{
		double indexStartTerm = x[index] - x[start];
		double indexStartTerm2 = y[index] - y[start];
		double computed = indexStartTerm;
		for (int i = 1; i < rawPowers.Length; i++)
		{
			rawPowers[i] += computed;
			if (i < rawDeltaYPowers.Length)
			{
				rawDeltaYPowers[i] += indexStartTerm2 * computed;
			}
			computed *= indexStartTerm;
		}
	}

	private static bool TryFitFromMoments(double[] x, double[] y, int start, int end, EndpointPolynomialOrder order, double[] rawPowers, double[] rawDeltaYPowers, double[] normalizedPowers, double[] normalizedDeltaYPowers, out EndpointPolynomialFit<double> fit)
	{
		if (order == EndpointPolynomialOrder.Linear)
		{
			fit = default;
			return true;
		}
		double endStartTerm = 1.0 / (x[end] - x[start]);
		double computed = endStartTerm;
		for (int i = 1; i < normalizedPowers.Length; i++)
		{
			normalizedPowers[i] = rawPowers[i] * computed;
			if (i < normalizedDeltaYPowers.Length)
			{
				normalizedDeltaYPowers[i] = rawDeltaYPowers[i] * computed;
			}
			computed *= endStartTerm;
		}
		double endStartTerm2 = y[end] - y[start];
		double normalizedPower = normalizedPowers[2];
		double normalizedPower2 = normalizedPowers[3];
		double normalizedPower3 = normalizedPowers[4];
		double normalizedPower3NormalizedPower2Term = normalizedPower3 - 2.0 * normalizedPower2 + normalizedPower;
		double normalizedDeltaYPowersNormalizedPower2Term = normalizedDeltaYPowers[2] - normalizedDeltaYPowers[1] - endStartTerm2 * (normalizedPower2 - normalizedPower);
		double computed2 = 64.0 * (Math.BitIncrement(1.0) - 1.0);
		if (order == EndpointPolynomialOrder.Quadratic)
		{
			double isFiniteTerm = ((double.IsFinite(normalizedPower3NormalizedPower2Term) && normalizedPower3NormalizedPower2Term > computed2) ? (normalizedDeltaYPowersNormalizedPower2Term / normalizedPower3NormalizedPower2Term) : 0.0);
			fit = new EndpointPolynomialFit<double>(isFiniteTerm, 0.0);
			if (double.IsFinite(isFiniteTerm))
			{
				return EndpointPolynomialFitter.IsMonotonic(endStartTerm2, fit);
			}
			return false;
		}
		double normalizedPower4 = normalizedPowers[5];
		double normalizedPower5 = normalizedPowers[6];
		double normalizedPower4NormalizedPower3Term = 2.0 * normalizedPower4 - 5.0 * normalizedPower3 + 4.0 * normalizedPower2 - normalizedPower;
		double normalizedPower5NormalizedPower4Term = 4.0 * normalizedPower5 - 12.0 * normalizedPower4 + 13.0 * normalizedPower3 - 6.0 * normalizedPower2 + normalizedPower;
		double normalizedDeltaYPowersNormalizedPower3Term = 2.0 * normalizedDeltaYPowers[3] - 3.0 * normalizedDeltaYPowers[2] + normalizedDeltaYPowers[1] - endStartTerm2 * (2.0 * normalizedPower3 - 3.0 * normalizedPower2 + normalizedPower);
		int endStartTerm3 = end - start - 1;
		double computed3 = normalizedPower3NormalizedPower2Term * normalizedPower5NormalizedPower4Term - normalizedPower4NormalizedPower3Term * normalizedPower4NormalizedPower3Term;
		double computed4 = computed2 * Math.Max(1.0, normalizedPower3NormalizedPower2Term * normalizedPower5NormalizedPower4Term);
		if (endStartTerm3 < 2 || !double.IsFinite(computed3) || computed3 <= computed4)
		{
			double isFiniteTerm2 = ((double.IsFinite(normalizedPower3NormalizedPower2Term) && normalizedPower3NormalizedPower2Term > computed2) ? (normalizedDeltaYPowersNormalizedPower2Term / normalizedPower3NormalizedPower2Term) : 0.0);
			fit = new EndpointPolynomialFit<double>(isFiniteTerm2, 0.0);
			if (double.IsFinite(isFiniteTerm2))
			{
				return EndpointPolynomialFitter.IsMonotonic(endStartTerm2, fit);
			}
			return false;
		}
		double computed5 = (normalizedDeltaYPowersNormalizedPower2Term * normalizedPower5NormalizedPower4Term - normalizedDeltaYPowersNormalizedPower3Term * normalizedPower4NormalizedPower3Term) / computed3;
		double computed6 = (normalizedDeltaYPowersNormalizedPower3Term * normalizedPower3NormalizedPower2Term - normalizedDeltaYPowersNormalizedPower2Term * normalizedPower4NormalizedPower3Term) / computed3;
		fit = new EndpointPolynomialFit<double>(computed5, computed6);
		if (double.IsFinite(computed5) && double.IsFinite(computed6))
		{
			return EndpointPolynomialFitter.IsMonotonic(endStartTerm2, fit);
		}
		return false;
	}

	private static double MaximumSegmentNormalizedError(double[] x, double[] y, double[] denominators, int start, int end, EndpointPolynomialFit<double> fit, double rejectionThreshold)
	{
		double pointErrorDenominatorsTerm = 0.0;
		double dx = x[end] - x[start];
		int endAboveStart = end - start;
		if (endAboveStart > 8)
		{
			pointErrorDenominatorsTerm = PointError(x, y, denominators, start, end, fit, start + endAboveStart / 2, dx);
			if (pointErrorDenominatorsTerm > rejectionThreshold + 1E-12)
			{
				return pointErrorDenominatorsTerm;
			}
			pointErrorDenominatorsTerm = Math.Max(pointErrorDenominatorsTerm, PointError(x, y, denominators, start, end, fit, start + endAboveStart / 4, dx));
			if (pointErrorDenominatorsTerm > rejectionThreshold + 1E-12)
			{
				return pointErrorDenominatorsTerm;
			}
			pointErrorDenominatorsTerm = Math.Max(pointErrorDenominatorsTerm, PointError(x, y, denominators, start, end, fit, start + 3 * endAboveStart / 4, dx));
			if (pointErrorDenominatorsTerm > rejectionThreshold + 1E-12)
			{
				return pointErrorDenominatorsTerm;
			}
		}
		for (int i = start + 1; i < end; i++)
		{
			pointErrorDenominatorsTerm = Math.Max(pointErrorDenominatorsTerm, PointError(x, y, denominators, start, end, fit, i, dx));
			if (pointErrorDenominatorsTerm > rejectionThreshold + 1E-12)
			{
				return pointErrorDenominatorsTerm;
			}
		}
		return pointErrorDenominatorsTerm;
	}

	private static double PointError(double[] x, double[] y, double[] denominators, int start, int end, EndpointPolynomialFit<double> fit, int index, double dx)
	{
		double t = (x[index] - x[start]) / dx;
		double indexEvaluateTerm = Math.Abs(y[index] - fit.Evaluate(y[start], y[end], t));
		if (denominators[index] > 0.0)
		{
			return indexEvaluateTerm / denominators[index];
		}
		if (indexEvaluateTerm != 0.0)
		{
			return double.PositiveInfinity;
		}
		return 0.0;
	}

	private static void ValidateInputs(double[] x, double[] y, double[] denominators, int[] requiredKnotIndexes, double rejectionThreshold)
	{
		if (x == null || y == null || denominators == null || requiredKnotIndexes == null)
		{
			throw new ArgumentNullException("x", "Catalog inputs cannot be null.");
		}
		if (x.Length != y.Length || x.Length != denominators.Length)
		{
			throw new ArgumentException("Catalog inputs must have equal lengths.");
		}
		if (!double.IsFinite(rejectionThreshold) || rejectionThreshold < 0.0)
		{
			throw new ArgumentOutOfRangeException("rejectionThreshold");
		}
		for (int i = 0; i < x.Length; i++)
		{
			if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]))
			{
				throw new ArgumentException("Catalog coordinates and values must be finite.");
			}
			if (!double.IsFinite(denominators[i]) || denominators[i] < 0.0)
			{
				throw new ArgumentException("Catalog denominators must be finite and nonnegative.");
			}
			if (i > 0 && x[i] <= x[i - 1])
			{
				throw new ArgumentException("Catalog coordinates must be strictly increasing.");
			}
		}
		int requiredKnotIndexTerm = -1;
		foreach (int requiredKnotIndex in requiredKnotIndexes)
		{
			if (requiredKnotIndex < 0 || requiredKnotIndex >= x.Length || requiredKnotIndex <= requiredKnotIndexTerm)
			{
				throw new ArgumentException("Required knots must be sorted, unique source indexes.", "requiredKnotIndexes");
			}
			requiredKnotIndexTerm = requiredKnotIndex;
		}
	}
}
