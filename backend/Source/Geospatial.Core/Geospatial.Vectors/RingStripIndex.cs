using System;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class RingStripIndex : IPointInPolygonChecker
{
	public const int VertexThreshold = 512;

	private readonly Polygon _ring;

	private readonly double _minY;

	private readonly double _invStripHeight;

	private readonly List<int>[] _strips;

	public RingStripIndex(Polygon ring)
	{
		_ring = ring;
		Extent extent = ring.Extent;
		int count = ring.Count;
		int count2 = Math.Clamp(count / 16, 1, 4096);
		_minY = extent.MinY;
		double maxYExtentTerm = Math.Max((extent.MaxY - extent.MinY) / (double)count2, double.Epsilon);
		_invStripHeight = 1.0 / maxYExtentTerm;
		_strips = new List<int>[count2];
		for (int i = 0; i < count; i++)
		{
			Point point = ring[i];
			Point point2 = ring[(i + 1) % count];
			int stripOfStart = StripOf(Math.Min(point.Y, point2.Y));
			int stripOfEnd = StripOf(Math.Max(point.Y, point2.Y));
			for (int j = stripOfStart; j <= stripOfEnd; j++)
			{
				List<int>[] strips = _strips;
				int ordinal = j;
				(strips[ordinal] ?? (strips[ordinal] = new List<int>())).Add(i);
			}
		}
	}

	private int StripOf(double y)
	{
		return Math.Clamp((int)((y - _minY) * _invStripHeight), 0, _strips.Length - 1);
	}

	public bool Contains(Point point)
	{
		List<int> strip = _strips[StripOf(point.Y)];
		if (strip == null)
		{
			return false;
		}
		bool condition = false;
		int count = _ring.Count;
		foreach (int strip2 in strip)
		{
			Point point2 = _ring[strip2];
			Point point3 = _ring[(strip2 + 1) % count];
			if (point2.Y > point.Y != point3.Y > point.Y && point2.X + (point.Y - point2.Y) / (point3.Y - point2.Y) * (point3.X - point2.X) > point.X)
			{
				condition = !condition;
			}
		}
		return condition;
	}
}
