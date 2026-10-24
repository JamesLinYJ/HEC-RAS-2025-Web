using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class VirtualMeshCellPolygonCollection : IReadOnlyList<Polygon>, IEnumerable<Polygon>, IEnumerable, IReadOnlyCollection<Polygon>
{
	private readonly Mesh _mesh;

	public int Count => _mesh.CellCount;

	public Polygon this[int index]
	{
		get
		{
			if (index >= Count)
			{
				throw new ArgumentOutOfRangeException();
			}
			return _mesh.CellPolygon(index);
		}
	}

	public VirtualMeshCellPolygonCollection(Mesh mesh)
	{
		_mesh = mesh;
	}

	public IEnumerator<Polygon> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Polygon> GetEnumerator_Internal()
	{
		for (int i = 0; i < Count; i++)
		{
			yield return _mesh.CellPolygon(i);
		}
	}
}
