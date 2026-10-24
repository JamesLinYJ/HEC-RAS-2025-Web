using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualTriangle4DCollection : IReadOnlyList<Triangle4D>, IEnumerable<Triangle4D>, IEnumerable, IReadOnlyCollection<Triangle4D>
{
	private readonly Tin _tin;

	public int Count => _tin.TriangleCount;

	public Triangle4D this[int index]
	{
		get
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[index];
			return new Triangle4D(_tin.Point4D[triangleDefinition.p0], _tin.Point4D[triangleDefinition.p1], _tin.Point4D[triangleDefinition.p2]);
		}
	}

	public VirtualTriangle4DCollection(Tin tin)
	{
		_tin = tin ?? throw new ArgumentNullException("tin");
	}

	public IEnumerator<Triangle4D> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Triangle4D> GetEnumerator_Internal()
	{
		if (_tin.TriangleCount != 0)
		{
			for (int i = 0; i < _tin.TriangleCount; i++)
			{
				TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
				yield return new Triangle4D(_tin.Point4D[triangleDefinition.p0], _tin.Point4D[triangleDefinition.p1], _tin.Point4D[triangleDefinition.p2]);
			}
		}
	}

	public Triangle4D First()
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[0];
		return new Triangle4D(_tin.Point4D[triangleDefinition.p0], _tin.Point4D[triangleDefinition.p1], _tin.Point4D[triangleDefinition.p2]);
	}

	public Triangle4D Last()
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[_tin.TriangleCount - 1];
		return new Triangle4D(_tin.Point4D[triangleDefinition.p0], _tin.Point4D[triangleDefinition.p1], _tin.Point4D[triangleDefinition.p2]);
	}

	public List<Triangle4D> ToList()
	{
		List<Triangle4D> triangleCount = new List<Triangle4D>(_tin.TriangleCount);
		for (int i = 0; i < _tin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
			triangleCount.Add(new Triangle4D(_tin.Point4D[triangleDefinition.p0], _tin.Point4D[triangleDefinition.p1], _tin.Point4D[triangleDefinition.p2]));
		}
		return triangleCount;
	}
}
