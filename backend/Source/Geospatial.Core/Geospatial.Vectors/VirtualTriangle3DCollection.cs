using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualTriangle3DCollection : IReadOnlyList<Triangle3D>, IEnumerable<Triangle3D>, IEnumerable, IReadOnlyCollection<Triangle3D>
{
	private readonly Tin _tin;

	public int Count => _tin.TriangleCount;

	public Triangle3D this[int index]
	{
		get
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[index];
			return new Triangle3D(_tin.Point3D[triangleDefinition.p0], _tin.Point3D[triangleDefinition.p1], _tin.Point3D[triangleDefinition.p2]);
		}
	}

	public VirtualTriangle3DCollection(Tin tin)
	{
		_tin = tin ?? throw new ArgumentNullException("tin");
	}

	public IEnumerator<Triangle3D> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Triangle3D> GetEnumerator_Internal()
	{
		if (_tin.TriangleCount != 0)
		{
			for (int i = 0; i < _tin.TriangleCount; i++)
			{
				TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
				yield return new Triangle3D(_tin.Point3D[triangleDefinition.p0], _tin.Point3D[triangleDefinition.p1], _tin.Point3D[triangleDefinition.p2]);
			}
		}
	}

	public Triangle3D First()
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[0];
		return new Triangle3D(_tin.Point3D[triangleDefinition.p0], _tin.Point3D[triangleDefinition.p1], _tin.Point3D[triangleDefinition.p2]);
	}

	public Triangle3D Last()
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[_tin.TriangleCount - 1];
		return new Triangle3D(_tin.Point3D[triangleDefinition.p0], _tin.Point3D[triangleDefinition.p1], _tin.Point3D[triangleDefinition.p2]);
	}

	public List<Triangle3D> ToList()
	{
		List<Triangle3D> triangleCount = new List<Triangle3D>(_tin.TriangleCount);
		for (int i = 0; i < _tin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
			triangleCount.Add(new Triangle3D(_tin.Point3D[triangleDefinition.p0], _tin.Point3D[triangleDefinition.p1], _tin.Point3D[triangleDefinition.p2]));
		}
		return triangleCount;
	}
}
