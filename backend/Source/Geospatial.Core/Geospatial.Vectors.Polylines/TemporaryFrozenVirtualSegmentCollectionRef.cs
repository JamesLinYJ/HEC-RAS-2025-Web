using System;
using System.Runtime.CompilerServices;

namespace Geospatial.Vectors.Polylines;

public ref struct TemporaryFrozenVirtualSegmentCollectionRef
{
	private Span<Point> _backingSpan;

	public readonly bool ConnectsEndToStart;

	public int Count
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

	public TemporaryFrozenVirtualSegmentCollectionRef(Polyline pline)
	{
		if (pline == null)
		{
			throw new ArgumentNullException("pline");
		}
		_backingSpan = pline.GetPointBufferUnsafe().Span;
		ConnectsEndToStart = pline is Polygon;
	}
}
