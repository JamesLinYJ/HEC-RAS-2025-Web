using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Systems.Shapes;

public interface IPolygon : IShape
{
	void SetCapacity(int capacity)
	{
	}

	void AddPoint(Point point);

	void AddPointRange(IList<Point> pts);

	void RemovePoint(Point point);

	void ClearPoints();
}
