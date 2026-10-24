using System.Collections.Generic;

namespace Utility.DataStructures;

public class DisjointSetUnion
{
	private readonly int[] _parent;

	public int Count => _parent.Length;

	public DisjointSetUnion(int count)
	{
		_parent = new int[count];
		for (int i = 0; i < count; i++)
		{
			_parent[i] = i;
		}
	}

	public int Find(int i)
	{
		int parent = i;
		while (_parent[parent] != parent)
		{
			parent = _parent[parent];
		}
		while (_parent[i] != parent)
		{
			int parent2 = _parent[i];
			_parent[i] = parent;
			i = parent2;
		}
		return parent;
	}

	public void Union(int a, int b)
	{
		int find = Find(a);
		int parent = Find(b);
		if (find != parent)
		{
			_parent[find] = parent;
		}
	}

	public bool AreConnected(int a, int b)
	{
		return Find(a) == Find(b);
	}

	public Dictionary<int, HashSet<int>> GetGroups()
	{
		Dictionary<int, HashSet<int>> groups = new Dictionary<int, HashSet<int>>();
		for (int i = 0; i < _parent.Length; i++)
		{
			int key = Find(i);
			if (!groups.TryGetValue(key, out var groupsTerm))
			{
				groupsTerm = (groups[key] = new HashSet<int>());
			}
			groupsTerm.Add(i);
		}
		return groups;
	}
}
