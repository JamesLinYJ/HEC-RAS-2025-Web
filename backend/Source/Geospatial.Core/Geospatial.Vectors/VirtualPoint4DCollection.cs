using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualPoint4DCollection : IReadOnlyList<Point4D>, IEnumerable<Point4D>, IEnumerable, IReadOnlyCollection<Point4D>
{
	private PointCollection _points;

	public int Count => _points.Count;

	public Point4D this[int index]
	{
		get
		{
			if (_points.HasZ && _points.HasM)
			{
				return new Point4D(_points[index], _points.Z[index], _points.M[index]);
			}
			if (_points.HasZ)
			{
				return new Point4D(_points[index], _points.Z[index], _points.MFillValue);
			}
			if (_points.HasM)
			{
				return new Point4D(_points[index], _points.ZFillValue, _points.M[index]);
			}
			return new Point4D(_points[index], _points.ZFillValue, _points.MFillValue);
		}
	}

	public VirtualPoint4DCollection(PointCollection pts)
	{
		_points = pts ?? throw new ArgumentNullException("pts");
	}

	public IEnumerator<Point4D> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Point4D> GetEnumerator_Internal()
	{
		if (_points.Count == 0)
		{
			yield break;
		}
		if (_points.HasZ && _points.HasM)
		{
			for (int i = 0; i < _points.Count; i++)
			{
				yield return new Point4D(_points[i], _points.Z[i], _points.M[i]);
			}
			yield break;
		}
		double mFill;
		if (_points.HasZ)
		{
			mFill = _points.MFillValue;
			for (int i = 0; i < _points.Count; i++)
			{
				yield return new Point4D(_points[i], _points.Z[i], mFill);
			}
			yield break;
		}
		if (_points.HasM)
		{
			mFill = _points.ZFillValue;
			for (int i = 0; i < _points.Count; i++)
			{
				yield return new Point4D(_points[i], mFill, _points.M[i]);
			}
			yield break;
		}
		mFill = _points.ZFillValue;
		double mFill2 = _points.MFillValue;
		for (int i = 0; i < _points.Count; i++)
		{
			yield return new Point4D(_points[i], mFill, mFill2);
		}
	}
}
