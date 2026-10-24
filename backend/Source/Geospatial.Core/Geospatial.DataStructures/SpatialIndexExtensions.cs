using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

public static class SpatialIndexExtensions
{
	public static void AddRange(this SpatialIndex<int> spIdx, IList<Point> points)
	{
		for (int i = 0; i < points.Count; i++)
		{
			spIdx.Add(points[i], i);
		}
	}

	public static void AddRangeIfInBounds(this SpatialIndex<int> spIdx, IList<Point> points)
	{
		for (int i = 0; i < points.Count; i++)
		{
			spIdx.AddInBounds(points[i], i);
		}
	}

	public static void AddRange(this SpatialIndex<int> spIdx, Memory<Point> points)
	{
		Span<Point> pointsSpan = points.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			spIdx.Add(pointsSpan[i], i);
		}
	}

	public static void AddRangeIfInBounds(this SpatialIndex<int> spIdx, Memory<Point> points)
	{
		Span<Point> pointsSpan = points.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			spIdx.AddInBounds(pointsSpan[i], i);
		}
	}
}
