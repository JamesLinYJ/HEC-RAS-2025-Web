using System;

namespace Geospatial.Vectors;

public struct ValuePolygon : ISimpleRing
{
	public Memory<Point> Points { get; }

	public Extent Extent { get; }

	public int SegmentCount => Points.Length;

	public int Count => Points.Length;

	public ValuePolygon(Memory<Point> points)
	{
		Points = points;
		Extent = PointCollection.ComputeExtent(points);
	}

	public Segment GetSegment(int segIdx)
	{
		Span<Point> points = Points.Span;
		return new Segment(points[segIdx], points[(segIdx + 1) % Count]);
	}

	public int NextPointIndex(int ptIdx)
	{
		return (ptIdx + 1) % Count;
	}

	public int PreviousPointIndex(int ptIdx)
	{
		return (Count + ptIdx - 1) % Count;
	}
}
