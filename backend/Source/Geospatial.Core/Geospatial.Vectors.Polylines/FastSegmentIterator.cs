using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Utility.Unsafe;

namespace Geospatial.Vectors.Polylines;

public ref struct FastSegmentIterator
{
	private Span<Point> _backingSpan;

	public readonly bool ConnectsEndToStart;

	public int CountWhenOpen => _backingSpan.Length - 1;

	public int SegmentCount
	{
		get
		{
			if (!ConnectsEndToStart)
			{
				return _backingSpan.Length - 1;
			}
			return _backingSpan.Length;
		}
	}

	public ref Segment this[int index] => ref Unsafe.As<Point, Segment>(ref _backingSpan[index]);

	public Segment GetClosingSegment()
	{
		return new Segment(_backingSpan[_backingSpan.Length - 1], _backingSpan[0]);
	}

	public FastSegmentIterator(Polyline pline)
	{
		_backingSpan = pline.GetPointBufferUnsafe().Span;
		ConnectsEndToStart = pline is Polygon;
	}

	public FastSegmentIterator(Polyline pline, bool isClosed)
	{
		_backingSpan = pline.GetPointBufferUnsafe().Span;
		ConnectsEndToStart = isClosed;
	}

	public FastSegmentIterator(List<Point> pline, bool isClosed)
	{
		_backingSpan = UnsafeHelpers.GetListBackingBuffer(pline).Span;
		ConnectsEndToStart = isClosed;
	}
}
