using System;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Matrices;

namespace Ras.Optimization;

public sealed class RBFSurrogateModel
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RBFSurrogateModel");

	private const double ResidualTolerance = 1E-06;

	private readonly double[][] _points;

	private readonly double[] _lambda;

	private readonly double[] _poly;

	private readonly int _dim;

	private RBFSurrogateModel(double[][] points, double[] lambda, double[] poly, int dim)
	{
		_points = points;
		_lambda = lambda;
		_poly = poly;
		_dim = dim;
	}

	public static bool TryFit(double[][] points, double[] values, double ridge, out RBFSurrogateModel model)
	{
		model = null;
		if (points == null || values == null)
		{
			throw new ArgumentNullException((points == null) ? "points" : "values");
		}
		if (points.Length != values.Length)
		{
			throw new ArgumentException("Points and values must have the same length.");
		}
		if (points.Length == 0)
		{
			return false;
		}
		int pointLength = points.Length;
		int pointLength2 = points[0].Length;
		if (pointLength < pointLength2 + 1)
		{
			return false;
		}
		for (int i = 0; i < pointLength; i++)
		{
			if (points[i] == null || points[i].Length != pointLength2)
			{
				throw new ArgumentException($"Point {i} does not have the uniform dimension {pointLength2}.", "points");
			}
			if (!double.IsFinite(values[i]))
			{
				throw new ArgumentException($"Fit value {i} is not finite; the caller must cap or exclude failures.");
			}
		}
		int count = pointLength + pointLength2 + 1;
		double[,] doubleValues = new double[count, count];
		double computed = 0.0;
		for (int j = 0; j < pointLength; j++)
		{
			for (int k = j + 1; k < pointLength; k++)
			{
				computed += (doubleValues[k, j] = (doubleValues[j, k] = Cubic(Distance(points[j], points[k]))));
			}
		}
		if (pointLength > 1)
		{
			computed /= (double)(pointLength * (pointLength - 1)) / 2.0;
		}
		for (int l = 0; l < pointLength; l++)
		{
			doubleValues[l, pointLength] = 1.0;
			doubleValues[pointLength, l] = 1.0;
			for (int m = 0; m < pointLength2; m++)
			{
				doubleValues[l, pointLength + 1 + m] = points[l][m];
				doubleValues[pointLength + 1 + m, l] = points[l][m];
			}
		}
		double[] doubleValuesArray = new double[count];
		Array.Copy(values, doubleValuesArray, pointLength);
		double ridgeTerm = ridge * Math.Max(1.0, computed);
		for (int n = 0; n < 3; n++)
		{
			double computed2 = ((n == 0) ? 0.0 : (ridgeTerm * Math.Pow(100.0, n - 1)));
			if (TrySolve(doubleValues, doubleValuesArray, pointLength, computed2, out var solution))
			{
				if (n > 0)
				{
					_logger.LogDebug("RBF fit needed ridge {Ridge:G3} ({Points} points, dim {Dim}).", computed2, pointLength, pointLength2);
				}
				double[] doubleValues2 = new double[pointLength];
				Array.Copy(solution, doubleValues2, pointLength);
				double[] doubleValues3 = new double[pointLength2 + 1];
				Array.Copy(solution, pointLength, doubleValues3, 0, pointLength2 + 1);
				double[][] doubleValues4 = new double[pointLength][];
				for (int intervalIndex = 0; intervalIndex < pointLength; intervalIndex++)
				{
					doubleValues4[intervalIndex] = (double[])points[intervalIndex].Clone();
				}
				model = new RBFSurrogateModel(doubleValues4, doubleValues2, doubleValues3, pointLength2);
				return true;
			}
		}
		_logger.LogWarning("RBF fit failed: system singular after ridge retries ({Points} points, dim {Dim}).", pointLength, pointLength2);
		return false;
	}

	public double Predict(double[] unitPoint)
	{
		double poly = _poly[0];
		for (int i = 0; i < _dim; i++)
		{
			poly += _poly[1 + i] * unitPoint[i];
		}
		for (int j = 0; j < _points.Length; j++)
		{
			poly += _lambda[j] * Cubic(Distance(_points[j], unitPoint));
		}
		return poly;
	}

	public double[] PredictMany(double[][] unitPoints)
	{
		double[] doubleValues = new double[unitPoints.Length];
		for (int i = 0; i < unitPoints.Length; i++)
		{
			doubleValues[i] = Predict(unitPoints[i]);
		}
		return doubleValues;
	}

	private static bool TrySolve(double[,] system, double[] rhs, int kernelSize, double appliedRidge, out double[] solution)
	{
		int rhsLength = rhs.Length;
		double[,] doubleValues = (double[,])system.Clone();
		for (int i = 0; i < kernelSize; i++)
		{
			doubleValues[i, i] += appliedRidge;
		}
		double[] b = (double[])rhs.Clone();
		solution = new double[rhsLength];
		Gauss<double>.SolveDenseInPlace(doubleValues, solution, b, rhsLength);
		double rhsTerm = 0.0;
		double rhsTerm2 = 1.0;
		for (int j = 0; j < rhsLength; j++)
		{
			if (!double.IsFinite(solution[j]))
			{
				return false;
			}
			rhsTerm2 = Math.Max(rhsTerm2, Math.Abs(rhs[j]));
			double kernelSizeAppliedRidgeTerm = ((j < kernelSize) ? (appliedRidge * solution[j]) : 0.0);
			for (int k = 0; k < rhsLength; k++)
			{
				kernelSizeAppliedRidgeTerm += system[j, k] * solution[k];
			}
			rhsTerm = Math.Max(rhsTerm, Math.Abs(kernelSizeAppliedRidgeTerm - rhs[j]));
		}
		return rhsTerm / rhsTerm2 <= 1E-06;
	}

	private static double Cubic(double r)
	{
		return r * r * r;
	}

	private static double Distance(double[] a, double[] b)
	{
		double computed = 0.0;
		for (int i = 0; i < a.Length; i++)
		{
			double computed2 = a[i] - b[i];
			computed += computed2 * computed2;
		}
		return Math.Sqrt(computed);
	}
}
