using System;

namespace Geospatial.Vectors;

public struct ImmutableMultiPartPolygon
{
	public readonly Memory<Point> Points;

	public readonly Memory<int> PolygonStartIndex;

	public int PolygonCount => PolygonStartIndex.Length;

	internal ImmutableMultiPartPolygon(Memory<Point> buf, int totalPolygonCount)
	{
		Points = buf;
		PolygonStartIndex = ((totalPolygonCount <= 0) ? Memory<int>.Empty : ((Memory<int>)new int[totalPolygonCount]));
	}

	internal ImmutableMultiPartPolygon(Memory<Point> points, Memory<int> polygonStartIndex)
	{
		Points = points;
		PolygonStartIndex = polygonStartIndex;
	}

	public static ImmutableMultiPartPolygonBuilder Create(int totalPts, Memory<Point> buf, int totalPolygonCount)
	{
		if (buf.Length != totalPts)
		{
			throw new ArgumentException();
		}
		return new ImmutableMultiPartPolygonBuilder(new ImmutableMultiPartPolygon(buf, totalPolygonCount));
	}

	public static ImmutableMultiPartPolygon CreateFromBuffers(Memory<Point> points, Memory<int> polygonStartIndices)
	{
		return new ImmutableMultiPartPolygon(points, polygonStartIndices);
	}

	public Memory<Point> GetPolygon(int index)
	{
		if (index < 0 || index >= PolygonCount)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		int polygonStartIndex = PolygonStartIndex.Span[index];
		int indexPolygonCountTerm = ((index + 1 == PolygonCount) ? Points.Length : PolygonStartIndex.Span[index + 1]) - polygonStartIndex;
		if (indexPolygonCountTerm <= 0)
		{
			return Memory<Point>.Empty;
		}
		return Points.Slice(polygonStartIndex, indexPolygonCountTerm);
	}
}
