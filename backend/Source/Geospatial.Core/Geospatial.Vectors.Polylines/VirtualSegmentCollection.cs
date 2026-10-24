using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors.Polylines;

internal class VirtualSegmentCollection : IReadOnlyList<Segment>, IEnumerable<Segment>, IEnumerable, IReadOnlyCollection<Segment>
{
	private IReadOnlyList<Point> _pline;

	public readonly bool ConnectsEndToStart;

	public int Count
	{
		get
		{
			if (!ConnectsEndToStart)
			{
				return _pline.Count - 1;
			}
			return _pline.Count;
		}
	}

	public Segment this[int index]
	{
		get
		{
			int count = Count;
			if (ConnectsEndToStart && index == count - 1)
			{
				return new Segment(_pline[count - 1], _pline[0]);
			}
			return new Segment(_pline[index], _pline[index + 1]);
		}
	}

	public VirtualSegmentCollection(IReadOnlyList<Point> pline)
	{
		_pline = pline ?? throw new ArgumentNullException("pline");
		ConnectsEndToStart = _pline is Polygon;
	}

	public IEnumerator<Segment> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Segment> GetEnumerator_Internal()
	{
		IReadOnlyList<Point> pts = _pline;
		if (pts.Count >= 2)
		{
			for (int i = 0; i < pts.Count - 1; i++)
			{
				yield return new Segment(pts[i], pts[i + 1]);
			}
			if (ConnectsEndToStart)
			{
				yield return new Segment(pts[Count - 1], pts[0]);
			}
		}
	}
}
