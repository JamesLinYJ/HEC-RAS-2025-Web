using System;

namespace Geospatial.Hydraulics.Analysis;

public static class HydraulicCurveDerivatives
{
	public static double[] ForwardDifference(double[] x, double[] y, double terminalDerivative)
	{
		if (x == null || y == null)
		{
			throw new ArgumentNullException("x", "Curve inputs cannot be null.");
		}
		if (x.Length != y.Length)
		{
			throw new ArgumentException("The x and y arrays must have equal lengths.");
		}
		double[] doubleValues = new double[y.Length];
		for (int i = 0; i < y.Length - 1; i++)
		{
			double computed = x[i + 1] - x[i];
			if (!double.IsFinite(computed) || computed <= 0.0)
			{
				throw new ArgumentException("The x values must be finite and strictly increasing.", "x");
			}
			doubleValues[i] = (y[i + 1] - y[i]) / computed;
		}
		if (doubleValues.Length != 0)
		{
			doubleValues[^1] = terminalDerivative;
		}
		return doubleValues;
	}

	public static double[] ForwardDifferenceAtKnots(double[] x, double[] y, int[] knotIndexes, double minimumTerminalDerivative)
	{
		if (x == null || y == null || knotIndexes == null)
		{
			throw new ArgumentNullException("x", "Curve inputs and knot indexes cannot be null.");
		}
		if (x.Length != y.Length || knotIndexes.Length < 2)
		{
			throw new ArgumentException("Curve arrays must match and at least two knots are required.");
		}
		double[] doubleValues = new double[knotIndexes.Length];
		double knotIndex2KnotIndexTerm = 0.0;
		for (int i = 0; i < knotIndexes.Length - 1; i++)
		{
			int knotIndex = knotIndexes[i];
			int knotIndex2 = knotIndexes[i + 1];
			if (knotIndex < 0 || knotIndex2 >= x.Length || knotIndex2 <= knotIndex)
			{
				throw new ArgumentException("Knot indexes must be sorted, unique source indexes.", "knotIndexes");
			}
			double knotIndex2KnotIndexTerm2 = x[knotIndex2] - x[knotIndex];
			if (!double.IsFinite(knotIndex2KnotIndexTerm2) || knotIndex2KnotIndexTerm2 <= 0.0)
			{
				throw new ArgumentException("Knot coordinates must be finite and strictly increasing.", "x");
			}
			knotIndex2KnotIndexTerm = (doubleValues[i] = (y[knotIndex2] - y[knotIndex]) / knotIndex2KnotIndexTerm2);
		}
		doubleValues[^1] = Math.Max(knotIndex2KnotIndexTerm, minimumTerminalDerivative);
		return doubleValues;
	}
}
