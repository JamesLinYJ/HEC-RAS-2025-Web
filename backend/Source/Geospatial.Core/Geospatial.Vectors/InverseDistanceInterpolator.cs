using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public class InverseDistanceInterpolator : ISpatialInterpolator
{
	private const double MinDistance = 1E-08;

	private double _maxReach;

	private IList<Point> _inputPoints;

	private ISpatialQuery<int> _query;

	private Extent _extHull = Extent.NullExtent;

	private Polygon _polyHull;

	public double MaxReach => _maxReach;

	public Extent ValidExtent { get; }

	public double InverseDistancePower { get; }

	public InverseDistanceInterpolator(double invDistancePower = 1.0, double maxReach = double.MaxValue)
	{
		if (maxReach <= 0.0)
		{
			throw new ArgumentException("Invalid max reach");
		}
		_maxReach = maxReach;
		InverseDistancePower = invDistancePower;
		ValidExtent = Extent.Infinite;
	}

	public InverseDistanceInterpolator(Extent hull, double invDistancePower = 1.0, double maxReach = double.MaxValue)
	{
		if (maxReach <= 0.0)
		{
			throw new ArgumentException("Invalid max reach");
		}
		_maxReach = maxReach;
		_extHull = hull;
		InverseDistancePower = invDistancePower;
		ValidExtent = hull;
	}

	public InverseDistanceInterpolator(Polygon hull, double invDistancePower = 1.0, double maxReach = double.MaxValue)
	{
		if (maxReach <= 0.0)
		{
			throw new ArgumentException("Invalid max reach");
		}
		_maxReach = maxReach;
		_polyHull = hull;
		InverseDistancePower = invDistancePower;
		ValidExtent = hull.Extent;
	}

	public void Initialize(IList<Point> points)
	{
		_inputPoints = points;
		if (points.Count < 25)
		{
			_query = new BruteForceVectorSpatialQuery(points);
		}
		else
		{
			_query = SpatialIndex.FromPoints(points);
		}
	}

	public void Query(Point queryPoint, out int[] indexes, out double[] weights)
	{
		if (!InBounds(queryPoint))
		{
			indexes = Array.Empty<int>();
			weights = Array.Empty<double>();
			return;
		}
		if (_maxReach == double.MaxValue)
		{
			ComputeAllWeights(queryPoint, out indexes, out weights);
			return;
		}
		List<int> elements = _query.GetElements(queryPoint, _maxReach, out var isApproximate);
		if (isApproximate)
		{
			ComputeWeightsWithDistanceCheck(queryPoint, elements, out indexes, out weights);
			return;
		}
		indexes = elements.ToArray();
		ComputeWeightsFor(queryPoint, ref indexes, out weights);
	}

	private void ComputeAllWeights(Point queryPoint, out int[] indexes, out double[] weights)
	{
		indexes = Enumerable.Range(0, _inputPoints.Count).ToArray();
		ComputeWeightsFor(queryPoint, ref indexes, out weights);
	}

	private void ComputeWeightsFor(Point queryPoint, ref int[] pointIndexes, out double[] weights)
	{
		weights = new double[pointIndexes.Length];
		double weights2Sum = 0.0;
		for (int i = 0; i < pointIndexes.Length; i++)
		{
			int pointIndex = pointIndexes[i];
			double distanceTo = _inputPoints[pointIndex].DistanceTo(queryPoint);
			if (distanceTo < 1E-08)
			{
				SetFullWeight(pointIndex, out pointIndexes, out weights);
				return;
			}
			double weights2 = 1.0 / distanceTo;
			if (InverseDistancePower != 1.0)
			{
				weights2 = Math.Pow(weights2, InverseDistancePower);
			}
			weights2Sum += weights2;
			weights[i] = weights2;
		}
		Normalize(weights, weights2Sum);
	}

	private void ComputeWeightsWithDistanceCheck(Point queryPoint, IList<int> checkIdxs, out int[] indexes, out double[] weights)
	{
		List<int> intList = new List<int>(checkIdxs.Count);
		List<double> doubleList = new List<double>(checkIdxs.Count);
		double scaledDistanceToSum = 0.0;
		for (int i = 0; i < checkIdxs.Count; i++)
		{
			int checkIdx = checkIdxs[i];
			double distanceTo = _inputPoints[checkIdx].DistanceTo(queryPoint);
			if (distanceTo < 1E-08)
			{
				SetFullWeight(i, out indexes, out weights);
				return;
			}
			if (distanceTo <= _maxReach)
			{
				intList.Add(checkIdx);
				double scaledDistanceTo = 1.0 / distanceTo;
				if (InverseDistancePower != 1.0)
				{
					scaledDistanceTo = Math.Pow(scaledDistanceTo, InverseDistancePower);
				}
				scaledDistanceToSum += scaledDistanceTo;
				doubleList.Add((float)scaledDistanceTo);
			}
		}
		indexes = intList.ToArray();
		weights = doubleList.ToArray();
		Normalize(weights, scaledDistanceToSum);
	}

	private static void SetFullWeight(int idx, out int[] indexes, out double[] weights)
	{
		indexes = new int[1];
		weights = new double[1];
		indexes[0] = idx;
		weights[0] = 1.0;
	}

	private bool InBounds(Point pt)
	{
		if (!_extHull.IsNull())
		{
			return _extHull.ContainsOrTouches(pt);
		}
		if (_polyHull != null)
		{
			return _polyHull.Contains(pt);
		}
		return true;
	}

	public static void ComputeWeights(Point queryPoint, IList<Point> inputs, out double[] weights, double invDistPower = 1.0)
	{
		weights = new double[inputs.Count];
		double weights2Sum = 0.0;
		for (int i = 0; i < inputs.Count; i++)
		{
			double distanceTo = inputs[i].DistanceTo(queryPoint);
			if (distanceTo < 1E-08)
			{
				for (int j = 0; j < inputs.Count; j++)
				{
					weights[j] = 0.0;
				}
				weights[i] = 1.0;
				return;
			}
			double weights2 = 1.0 / distanceTo;
			if (invDistPower != 1.0)
			{
				weights2 = Math.Pow(weights2, invDistPower);
			}
			weights2Sum += weights2;
			weights[i] = weights2;
		}
		Normalize(weights, weights2Sum);
	}

	private static void Normalize(double[] weights, double sum)
	{
		for (int i = 0; i < weights.Length; i++)
		{
			weights[i] /= sum;
		}
	}
}
