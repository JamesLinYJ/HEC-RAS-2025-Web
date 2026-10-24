using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

public class BruteForceVectorSpatialQuery : ISpatialQuery<int>
{
	private IList<Point> _points;

	public IList<Point> Points => _points;

	public BruteForceVectorSpatialQuery(IList<Point> points)
	{
		_points = points;
	}

	public List<int> GetElements(Point pt, double distance, out bool isApproximate)
	{
		List<int> elements = new List<int>();
		for (int i = 0; i < _points.Count; i++)
		{
			if (_points[i].CloseToXY(pt, distance))
			{
				elements.Add(i);
			}
		}
		isApproximate = false;
		return elements;
	}

	public List<int> GetElements(Extent extent, out bool isApproximate)
	{
		List<int> elements = new List<int>();
		for (int i = 0; i < _points.Count; i++)
		{
			if (extent.Contains(_points[i]))
			{
				elements.Add(i);
			}
		}
		isApproximate = false;
		return elements;
	}
}
