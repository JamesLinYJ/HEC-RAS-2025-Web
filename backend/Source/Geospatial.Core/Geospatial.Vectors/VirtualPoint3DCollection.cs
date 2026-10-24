using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualPoint3DCollection : IReadOnlyList<Point3D>, IEnumerable<Point3D>, IEnumerable, IReadOnlyCollection<Point3D>
{
	private readonly PointCollection _points;

	public int Count => _points.Count;

	public IList<Point3D> BackingBuffer
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public Point3D this[int index]
	{
		get
		{
			if (_points.HasZ)
			{
				return new Point3D(_points[index], _points.Z[index]);
			}
			return new Point3D(_points[index], _points.ZFillValue);
		}
	}

	public VirtualPoint3DCollection(PointCollection pts)
	{
		_points = pts ?? throw new ArgumentNullException("pts");
	}

	public IEnumerator<Point3D> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Point3D> GetEnumerator_Internal()
	{
		if (_points.Count == 0)
		{
			yield break;
		}
		if (_points.HasZ)
		{
			for (int i = 0; i < _points.Count; i++)
			{
				yield return new Point3D(_points[i], _points.Z[i]);
			}
			yield break;
		}
		double fill = _points.ZFillValue;
		for (int i = 0; i < _points.Count; i++)
		{
			yield return new Point3D(_points[i], fill);
		}
	}

	public Point3D First()
	{
		throw new NotImplementedException();
	}

	public Point3D Last()
	{
		throw new NotImplementedException();
	}

	public List<Point3D> ToList()
	{
		throw new NotImplementedException();
	}
}
