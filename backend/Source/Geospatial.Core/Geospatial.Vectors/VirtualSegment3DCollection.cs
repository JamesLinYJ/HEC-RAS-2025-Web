using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualSegment3DCollection : IReadOnlyList<Segment3D>, IEnumerable<Segment3D>, IEnumerable, IReadOnlyCollection<Segment3D>
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

	public Segment3D this[int index]
	{
		get
		{
			int count = Count;
			if (ConnectsEndToStart && index == count - 1)
			{
				return new Segment3D(_pline.Point3D[count - 1], _pline.Point3D[0]);
			}
			return new Segment3D(_pline.Point3D[index], _pline.Point3D[index + 1]);
		}
	}

	public VirtualSegment3DCollection(Polyline pline)
	{
		_pline = pline ?? throw new ArgumentNullException("pline");
		ConnectsEndToStart = _pline is Polygon;
	}

	public IEnumerator<Segment3D> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Segment3D> GetEnumerator_Internal()
	{
		int ct = _pline.Count;
		if (ct >= 2)
		{
			for (int i = 0; i < ct - 1; i++)
			{
				yield return new Segment3D(_pline.Point3D[i], _pline.Point3D[i + 1]);
			}
			if (ConnectsEndToStart)
			{
				yield return new Segment3D(_pline.Point3D[ct - 1], _pline.Point3D[0]);
			}
		}
	}
}
