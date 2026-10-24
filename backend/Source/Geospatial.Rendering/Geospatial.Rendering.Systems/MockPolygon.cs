using System.Collections.Generic;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockPolygon : MockShape, Geospatial.Systems.Shapes.IPolygon, IShape
{
	public List<Point> PointsList { get; } = new List<Point>();

	public IList<Point> Points => PointsList;

	public MockPolygon()
	{
	}

	public MockPolygon(double wd, double ht)
		: base(wd, ht)
	{
	}

	public void AddPoint(Point point)
	{
		Points.Add(point);
	}

	public void RemovePoint(Point point)
	{
		Points.Remove(point);
	}

	public void ClearPoints()
	{
		Points.Clear();
	}

	public void AddPointRange(IList<Point> pts)
	{
		PointsList.AddRange(pts);
	}
}
