using System;
using System.Collections.Generic;

namespace Utility.Matrices;

public class RCM
{
	public int[] Perm;

	public int[] PermInverse;

	public RCM()
	{
		Perm = null;
		PermInverse = null;
	}

	public void Compute(int n, int[] rowPtr, int[] colInd, bool usePseudoPeripheral = true)
	{
		if (n < 0)
		{
			throw new ArgumentException("n must be non-negative.");
		}
		if (rowPtr == null || colInd == null)
		{
			throw new ArgumentNullException();
		}
		if (rowPtr.Length < n + 1)
		{
			throw new ArgumentException("rowPtr length must be at least n+1.");
		}
		List<int>[] adjacency = BuildAdjacency(n, rowPtr, colInd);
		int[] intValues = new int[n];
		for (int i = 0; i < n; i++)
		{
			intValues[i] = adjacency[i].Count;
		}
		bool[] boolValues = new bool[n];
		List<int> intList = new List<int>(n);
		List<int> cuthillMcKeeBfs;
		for (int j = 0; j < n; j += cuthillMcKeeBfs.Count)
		{
			int unvisitedMinDegree = FindUnvisitedMinDegree(boolValues, intValues);
			if (unvisitedMinDegree == -1)
			{
				break;
			}
			if (usePseudoPeripheral)
			{
				unvisitedMinDegree = FindPseudoPeripheral(unvisitedMinDegree, adjacency, intValues, boolValues);
			}
			cuthillMcKeeBfs = CuthillMcKeeBfs(unvisitedMinDegree, adjacency, intValues, boolValues);
			cuthillMcKeeBfs.Reverse();
			intList.AddRange(cuthillMcKeeBfs);
		}
		Perm = intList.ToArray();
		PermInverse = new int[n];
		for (int k = 0; k < n; k++)
		{
			PermInverse[Perm[k]] = k;
		}
	}

	private static List<int>[] BuildAdjacency(int n, int[] rowPtr, int[] colInd)
	{
		List<int>[] adjacency = new List<int>[n];
		for (int i = 0; i < n; i++)
		{
			adjacency[i] = new List<int>();
		}
		for (int j = 0; j < n; j++)
		{
			int rowPtrStart = rowPtr[j];
			int rowPtrEnd = rowPtr[j + 1];
			for (int k = rowPtrStart; k < rowPtrEnd; k++)
			{
				int colInd2 = colInd[k];
				if (colInd2 >= 0 && colInd2 < n && colInd2 != j)
				{
					adjacency[j].Add(colInd2);
					adjacency[colInd2].Add(j);
				}
			}
		}
		for (int l = 0; l < n; l++)
		{
			List<int> adjacencyList = adjacency[l];
			if (adjacencyList.Count <= 1)
			{
				if (adjacencyList.Count == 1 && adjacencyList[0] == l)
				{
					adjacencyList.Clear();
				}
				continue;
			}
			adjacencyList.Sort();
			int unity = 1;
			for (int m = 1; m < adjacencyList.Count; m++)
			{
				if (adjacencyList[m] != adjacencyList[m - 1])
				{
					adjacencyList[unity++] = adjacencyList[m];
				}
			}
			if (unity < adjacencyList.Count)
			{
				adjacencyList.RemoveRange(unity, adjacencyList.Count - unity);
			}
		}
		return adjacency;
	}

	private static int FindUnvisitedMinDegree(bool[] visited, int[] deg)
	{
		int visitedLength = visited.Length;
		int result = -1;
		int maxValue = int.MaxValue;
		for (int i = 0; i < visitedLength; i++)
		{
			if (!visited[i] && deg[i] < maxValue)
			{
				maxValue = deg[i];
				result = i;
			}
		}
		return result;
	}

	private static int FindPseudoPeripheral(int s, List<int>[] adj, int[] deg, bool[] blocked)
	{
		int pseudoPeripheral = s;
		int countTerm = -1;
		while (true)
		{
			List<List<int>> bfsLevels = BuildBfsLevels(pseudoPeripheral, adj, blocked);
			int count = bfsLevels.Count;
			if (count <= countTerm || count == 0)
			{
				break;
			}
			countTerm = count;
			List<int> bfsLevel = bfsLevels[count - 1];
			int bfsLevel2 = bfsLevel[0];
			for (int i = 1; i < bfsLevel.Count; i++)
			{
				int bfsLevel3 = bfsLevel[i];
				if (deg[bfsLevel3] < deg[bfsLevel2] || (deg[bfsLevel3] == deg[bfsLevel2] && bfsLevel3 < bfsLevel2))
				{
					bfsLevel2 = bfsLevel3;
				}
			}
			if (bfsLevel2 == pseudoPeripheral)
			{
				break;
			}
			pseudoPeripheral = bfsLevel2;
		}
		return pseudoPeripheral;
	}

	private static List<List<int>> BuildBfsLevels(int start, List<int>[] adj, bool[] blocked)
	{
		int adjLength = adj.Length;
		List<List<int>> bfsLevels = new List<List<int>>();
		if (blocked[start])
		{
			return bfsLevels;
		}
		bool[] boolValues = new bool[adjLength];
		Queue<int> intQueue = new Queue<int>();
		boolValues[start] = true;
		intQueue.Enqueue(start);
		while (intQueue.Count > 0)
		{
			int count = intQueue.Count;
			List<int> intList = new List<int>(count);
			List<int> intListList = new List<int>();
			for (int i = 0; i < count; i++)
			{
				int dequeue = intQueue.Dequeue();
				intList.Add(dequeue);
				foreach (int adj2 in adj[dequeue])
				{
					if (!blocked[adj2] && !boolValues[adj2])
					{
						boolValues[adj2] = true;
						intListList.Add(adj2);
					}
				}
			}
			bfsLevels.Add(intList);
			if (intListList.Count == 0)
			{
				break;
			}
			foreach (int ordinal in intListList)
			{
				intQueue.Enqueue(ordinal);
			}
		}
		return bfsLevels;
	}

	private static List<int> CuthillMcKeeBfs(int start, List<int>[] adj, int[] deg, bool[] visited)
	{
		List<int> intList = new List<int>();
		Queue<int> intQueue = new Queue<int>();
		visited[start] = true;
		intQueue.Enqueue(start);
		while (intQueue.Count > 0)
		{
			int dequeue = intQueue.Dequeue();
			intList.Add(dequeue);
			List<int> intListList = new List<int>();
			foreach (int adj2 in adj[dequeue])
			{
				if (!visited[adj2])
				{
					intListList.Add(adj2);
				}
			}
			intListList.Sort((int a, int b) =>
			{
				int degTerm = deg[a].CompareTo(deg[b]);
				return (degTerm != 0) ? degTerm : a.CompareTo(b);
			});
			foreach (int ordinal in intListList)
			{
				visited[ordinal] = true;
				intQueue.Enqueue(ordinal);
			}
		}
		return intList;
	}
}
