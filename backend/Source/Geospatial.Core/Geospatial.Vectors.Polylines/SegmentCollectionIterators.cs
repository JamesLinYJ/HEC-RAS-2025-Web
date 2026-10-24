using System;
using System.Runtime.CompilerServices;

namespace Geospatial.Vectors.Polylines;

public static class SegmentCollectionIterators
{
	public static T Injectable<T>(Func<Segment, T, T> gonext, Polyline pl, T initState)
	{
		Span<Point> pointBufferUnsafe = pl.GetPointBufferUnsafe().Span;
		bool plWithPolygon = pl is Polygon;
		int count = pl.PointCount - ((!plWithPolygon) ? 1 : 2);
		T initState2 = initState;
		for (int i = 0; i < count; i++)
		{
			initState2 = gonext(Unsafe.As<Point, Segment>(ref pointBufferUnsafe[i]), initState2);
		}
		if (plWithPolygon)
		{
			initState2 = gonext(new Segment(pointBufferUnsafe[pointBufferUnsafe.Length - 1], pointBufferUnsafe[0]), initState2);
		}
		return initState2;
	}

	public static T InjectableWithContinue<T>(Func<Segment, T, (T, bool)> gonext, Polyline pl, T initState)
	{
		Span<Point> pointBufferUnsafe = pl.GetPointBufferUnsafe().Span;
		int segmentCount = pl.SegmentCount;
		T initState2 = initState;
		for (int i = 0; i < segmentCount; i++)
		{
			(T, bool) tuple = gonext(Unsafe.As<Point, Segment>(ref pointBufferUnsafe[i]), initState2);
			var (initState22, _) = tuple;
			if (!tuple.Item2)
			{
				return initState22;
			}
			initState2 = initState22;
		}
		return initState2;
	}

	public static T InjectableWithContinue<T, K>(Func<Segment, T, K, (T, bool)> gonext, Polyline pl, K model, T initState)
	{
		Span<Point> pointBufferUnsafe = pl.GetPointBufferUnsafe().Span;
		int segmentCount = pl.SegmentCount;
		T initState2 = initState;
		for (int i = 0; i < segmentCount; i++)
		{
			(T, bool) tuple = gonext(Unsafe.As<Point, Segment>(ref pointBufferUnsafe[i]), initState2, model);
			var (initState22, _) = tuple;
			if (!tuple.Item2)
			{
				return initState22;
			}
			initState2 = initState22;
		}
		return initState2;
	}

	public static T InjectableWithContinuePreSpanned<T, K>(Func<Segment, T, K, (T, bool)> gonext, Span<Point> ptSpan, K model, T initState)
	{
		int length = ptSpan.Length;
		T initState2 = initState;
		for (int i = 0; i < length; i++)
		{
			(T, bool) tuple = gonext(Unsafe.As<Point, Segment>(ref ptSpan[i]), initState2, model);
			var (initState22, _) = tuple;
			if (!tuple.Item2)
			{
				return initState22;
			}
			initState2 = initState22;
		}
		return initState2;
	}
}
