using System.Collections.Generic;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockLine : MockShape, ILine, IShape
{
	private List<Point> _pts = new List<Point>();

	public IReadOnlyList<Point> Points => _pts;

	public int Count => _pts.Count;

	public bool IsFilled { get; set; }

	public bool IsClosed { get; set; }

	public MockLine()
	{
	}

	public MockLine(double wd, double ht)
		: base(wd, ht)
	{
	}

	public void AddPoint(double x, double y)
	{
		_pts.Add(new Point(x, y));
	}

	public void AddPoint(Point pt)
	{
		_pts.Add(pt);
	}

	public void AddPointRange(IList<Point> pts)
	{
		_pts.AddRange(pts);
	}

	public void ClearPoints()
	{
		_pts.Clear();
	}

	public Point GetPoint(int idx)
	{
		return _pts[idx];
	}

	public void RemovePoint(int idx)
	{
		_pts.RemoveAt(idx);
	}

	public void UpdatePoint(int idx, double x, double y)
	{
		_pts[idx] = new Point(x, y);
	}

	public void UpdatePoint(int idx, Point pt)
	{
		_pts[idx] = pt;
	}

	public void TrimPointsFromEnd(int count)
	{
		_pts.RemoveRange(Count - count - 1, count);
	}
}
