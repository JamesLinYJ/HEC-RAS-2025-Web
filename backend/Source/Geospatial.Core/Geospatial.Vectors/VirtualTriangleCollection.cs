using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualTriangleCollection : IReadOnlyList<Triangle>, IEnumerable<Triangle>, IEnumerable, IReadOnlyCollection<Triangle>
{
	private Tin _tin;

	public int Count => _tin.TriangleCount;

	public Triangle this[int index]
	{
		get
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[index];
			return new Triangle(_tin.Points[triangleDefinition.p0], _tin.Points[triangleDefinition.p1], _tin.Points[triangleDefinition.p2]);
		}
	}

	public VirtualTriangleCollection(Tin tin)
	{
		_tin = tin ?? throw new ArgumentNullException("tin");
	}

	public IEnumerator<Triangle> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Triangle> GetEnumerator_Internal()
	{
		if (_tin.TriangleCount != 0)
		{
			for (int i = 0; i < _tin.TriangleCount; i++)
			{
				TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
				yield return new Triangle(_tin.Points[triangleDefinition.p0], _tin.Points[triangleDefinition.p1], _tin.Points[triangleDefinition.p2]);
			}
		}
	}

	public Triangle First()
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[0];
		return new Triangle(_tin.Points[triangleDefinition.p0], _tin.Points[triangleDefinition.p1], _tin.Points[triangleDefinition.p2]);
	}

	public Triangle Last()
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[_tin.TriangleCount - 1];
		return new Triangle(_tin.Points[triangleDefinition.p0], _tin.Points[triangleDefinition.p1], _tin.Points[triangleDefinition.p2]);
	}

	public List<Triangle> ToList()
	{
		List<Triangle> triangleCount = new List<Triangle>(_tin.TriangleCount);
		for (int i = 0; i < _tin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
			triangleCount.Add(new Triangle(_tin.Points[triangleDefinition.p0], _tin.Points[triangleDefinition.p1], _tin.Points[triangleDefinition.p2]));
		}
		return triangleCount;
	}
}
