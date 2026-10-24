namespace Geospatial.Vectors.MeshTopology;

public interface ISizeFunction
{
	double Query(Point point, bool checkInside);

	double Query(Point point)
	{
		return Query(point, checkInside: false);
	}

	void QueryRange(Point point, bool checkInside, out double maximum, out double minimum);
}
