using System;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class TakeNearestInterpolator : ISpatialInterpolator
{
	private IList<Point> _points;

	public Extent ValidExtent
	{
		get
		{
			if (_points != null)
			{
				return Extent.Union(_points);
			}
			return Extent.NullExtent;
		}
	}

	public void Initialize(IList<Point> points)
	{
		_points = points;
	}

	public void Query(Point queryPoint, out int[] indexes, out double[] weights)
	{
		double maxValue = double.MaxValue;
		int indexes2 = -1;
		for (int i = 0; i < _points.Count; i++)
		{
			double distanceTo = _points[i].DistanceTo(queryPoint);
			if (distanceTo < maxValue)
			{
				maxValue = distanceTo;
				indexes2 = i;
			}
		}
		if (indexes2 == -1)
		{
			indexes = Array.Empty<int>();
			weights = Array.Empty<double>();
			return;
		}
		indexes = new int[1];
		weights = new double[1];
		indexes[0] = indexes2;
		weights[0] = 1.0;
	}
}
