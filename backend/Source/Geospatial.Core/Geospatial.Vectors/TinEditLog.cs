using System;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class TinEditLog
{
	private HashSet<Tuple<int, int>> _deletedEdges = new HashSet<Tuple<int, int>>();

	private HashSet<Tuple<int, int>> _createdEdges = new HashSet<Tuple<int, int>>();

	private HashSet<Tuple<int, int, int>> _createdTriangles = new HashSet<Tuple<int, int, int>>();

	private Dictionary<Tuple<int, int, int>, bool> _deletedTriangles = new Dictionary<Tuple<int, int, int>, bool>();

	public IEnumerable<Tuple<int, int>> CreatedEdges => _createdEdges;

	public IEnumerable<Tuple<int, int>> DeletedEdges => _deletedEdges;

	public IEnumerable<Tuple<int, int, int>> CreatedTriangles => _createdTriangles;

	public Dictionary<Tuple<int, int, int>, bool> DeletedTriangles => _deletedTriangles;

	public void LogDeleteEdge(int pA, int pB)
	{
		Tuple<int, int> pAPBTerm = Key(pA, pB);
		if (_createdEdges.Contains(pAPBTerm))
		{
			_createdEdges.Remove(pAPBTerm);
		}
		else
		{
			_deletedEdges.Add(pAPBTerm);
		}
	}

	public bool EdgeWasDeleted(int pA, int pB)
	{
		return _deletedEdges.Contains(Key(pA, pB));
	}

	public void LogAddEdge(int pA, int pB)
	{
		Tuple<int, int> pAPBTerm = Key(pA, pB);
		if (_deletedEdges.Contains(pAPBTerm))
		{
			_deletedEdges.Remove(pAPBTerm);
		}
		else
		{
			_createdEdges.Add(pAPBTerm);
		}
	}

	public void LogDeleteTriangle(TriangleDefinition triDef)
	{
		Tuple<int, int, int> tuple = Key(triDef);
		if (_createdTriangles.Contains(tuple))
		{
			_createdTriangles.Remove(tuple);
		}
		else
		{
			_deletedTriangles[tuple] = triDef.IsDeleted;
		}
	}

	public void LogAddTriangle(TriangleDefinition triDef)
	{
		Tuple<int, int, int> tuple = Key(triDef);
		if (_deletedTriangles.ContainsKey(tuple))
		{
			_deletedTriangles.Remove(tuple);
		}
		else
		{
			_createdTriangles.Add(tuple);
		}
	}

	private Tuple<int, int> Key(int pA, int pB)
	{
		if (pA < pB)
		{
			return new Tuple<int, int>(pA, pB);
		}
		return new Tuple<int, int>(pB, pA);
	}

	private Tuple<int, int, int> Key(TriangleDefinition triDef)
	{
		if (triDef.p0 < triDef.p1 && triDef.p0 < triDef.p2)
		{
			return new Tuple<int, int, int>(triDef.p0, triDef.p1, triDef.p2);
		}
		if (triDef.p1 < triDef.p0 && triDef.p1 < triDef.p2)
		{
			return new Tuple<int, int, int>(triDef.p1, triDef.p2, triDef.p0);
		}
		return new Tuple<int, int, int>(triDef.p2, triDef.p0, triDef.p1);
	}

	public void Clear()
	{
		_deletedEdges.Clear();
		_createdEdges.Clear();
		_deletedTriangles.Clear();
		_createdTriangles.Clear();
	}
}
