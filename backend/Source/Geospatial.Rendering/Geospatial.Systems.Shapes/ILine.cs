using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Systems.Shapes;

public interface ILine : IShape
{
	int Count { get; }

	void SetCapacity(int capacity)
	{
	}

	void AddPoint(double x, double y);

	void AddPoint(Point pt);

	void AddPointRange(IList<Point> pts);

	Point GetPoint(int idx);

	void RemovePoint(int idx);

	void UpdatePoint(int idx, double x, double y);

	void UpdatePoint(int idx, Point pt);

	void ClearPoints();

	void TrimPointsFromEnd(int count);
}
