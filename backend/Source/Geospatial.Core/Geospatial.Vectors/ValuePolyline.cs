using System;

namespace Geospatial.Vectors;

public struct ValuePolyline
{
	public Memory<Point> Points { get; }

	public Extent Extent { get; }

	public int SegmentCount => Math.Max(0, Points.Length - 1);

	public int Count => Points.Length;

	public ValuePolyline(Memory<Point> points)
	{
		Points = points;
		Extent = PointCollection.ComputeExtent(points);
	}

	public Segment GetSegment(int segIdx)
	{
		if (segIdx < 0 || segIdx >= SegmentCount)
		{
			throw new ArgumentOutOfRangeException("segIdx");
		}
		Span<Point> points = Points.Span;
		return new Segment(points[segIdx], points[segIdx + 1]);
	}
}
