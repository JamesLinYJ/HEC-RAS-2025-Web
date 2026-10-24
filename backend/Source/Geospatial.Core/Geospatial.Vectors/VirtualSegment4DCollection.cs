using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualSegment4DCollection : IReadOnlyList<Segment4D>, IEnumerable<Segment4D>, IEnumerable, IReadOnlyCollection<Segment4D>
{
	private Polyline _pline;

	public readonly bool ConnectsEndToStart;

	public int Count
	{
		get
		{
			if (!ConnectsEndToStart)
			{
				return _pline.PointCount - 1;
			}
			return _pline.PointCount;
		}
	}

	public Segment4D this[int index]
	{
		get
		{
			int count = Count;
			if (ConnectsEndToStart && index == count - 1)
			{
				return new Segment4D(_pline.Point4D[count - 1], _pline.Point4D[0]);
			}
			return new Segment4D(_pline.Point4D[index], _pline.Point4D[index + 1]);
		}
	}

	public VirtualSegment4DCollection(Polyline pline)
	{
		_pline = pline ?? throw new ArgumentNullException("pline");
		ConnectsEndToStart = _pline is Polygon;
	}

	public IEnumerator<Segment4D> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Segment4D> GetEnumerator_Internal()
	{
		int ct = _pline.Count;
		if (ct >= 2)
		{
			for (int i = 0; i < ct - 1; i++)
			{
				yield return new Segment4D(_pline.Point4D[i], _pline.Point4D[i + 1]);
			}
			if (ConnectsEndToStart)
			{
				yield return new Segment4D(_pline.Point4D[ct - 1], _pline.Point4D[0]);
			}
		}
	}
}
