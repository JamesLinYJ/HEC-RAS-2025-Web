using System;
using System.Collections.Generic;
using System.Linq;
using Utility.DataStructures;
using Utility.Systems;

namespace Geospatial.Vectors;

public static class PolylineMergeHelpers
{
	public readonly struct MergeResult
	{
		public static MergeResult Empty => new MergeResult
		{
			MergedPolylines = new List<Polyline>(),
			UnmergedIndices = new List<int>()
		};

		public static MergeResult SinglePolyline => new MergeResult
		{
			MergedPolylines = new List<Polyline>(),
			UnmergedIndices = new List<int> { 0 }
		};

		public List<Polyline> MergedPolylines { get; init; }

		public List<int> UnmergedIndices { get; init; }
	}

	public readonly struct EndpointConnection
	{
		public static EndpointConnection None => new EndpointConnection
		{
			IndexA = -1,
			IndexB = -1
		};

		public bool IsValid
		{
			get
			{
				if (IndexA >= 0)
				{
					return IndexB >= 0;
				}
				return false;
			}
		}

		public int IndexA { get; init; }

		public bool IsStartA { get; init; }

		public int IndexB { get; init; }

		public bool IsStartB { get; init; }

		public double Distance { get; init; }
	}

	public static readonly KeyCode MergeKey = KeyCode.M;

	public const string ChannelPrefix = "polyline_merge";

	public static List<int> FindPolylinesIn(Polygon region, IPolylineCollection collection)
	{
		if (region == null || collection == null)
		{
			return new List<int>();
		}
		List<int> elements = collection.GetSpatialIndex().GetElements(region.Extent);
		List<int> intList = new List<int>(elements.Count);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			if (region.ContainsOrIntersects(collection[element]))
			{
				intList.Add(element);
			}
		}
		return intList;
	}

	public static MergeResult MergePolylines(IReadOnlyList<Polyline> polylines, double tolerance, MergeMode mode = MergeMode.RejectBranching)
	{
		if (polylines == null || polylines.Count == 0)
		{
			return MergeResult.Empty;
		}
		if (polylines.Count == 1)
		{
			return MergeResult.SinglePolyline;
		}
		if (double.IsNaN(tolerance) || tolerance < 0.0)
		{
			List<int> unmergedIndices = new List<int>(polylines.Count);
			for (int i = 0; i < polylines.Count; i++)
			{
				unmergedIndices.Add(i);
			}
			return new MergeResult
			{
				MergedPolylines = new List<Polyline>(),
				UnmergedIndices = unmergedIndices
			};
		}
		List<EndpointConnection> connections = FindConnections(polylines, tolerance);
		DisjointSetUnion disjointSetUnion = new DisjointSetUnion(polylines.Count);
		foreach (EndpointConnection connection in connections)
		{
			disjointSetUnion.Union(connection.IndexA, connection.IndexB);
		}
		Dictionary<int, HashSet<int>> groups = disjointSetUnion.GetGroups();
		List<Polyline> mergedPolylines = new List<Polyline>();
		List<int> unmergedIndicesList = new List<int>();
		foreach (HashSet<int> localValue in groups.Values)
		{
			if (localValue.Count == 1)
			{
				unmergedIndicesList.Add(localValue.First());
				continue;
			}
			List<EndpointConnection> endpointConnections = new List<EndpointConnection>();
			foreach (EndpointConnection connection2 in connections)
			{
				if (localValue.Contains(connection2.IndexA))
				{
					endpointConnections.Add(connection2);
				}
			}
			HashSet<int> localValueSet = new HashSet<int>(localValue);
			Polyline chain;
			List<int> mergedIndices;
			while (localValueSet.Count > 1 && TryBuildChain(polylines, localValueSet, endpointConnections, mode, out chain, out mergedIndices) && mergedIndices.Count >= 2)
			{
				mergedPolylines.Add(chain);
				foreach (int mergedIndex in mergedIndices)
				{
					localValueSet.Remove(mergedIndex);
				}
			}
			unmergedIndicesList.AddRange(localValueSet);
		}
		return new MergeResult
		{
			MergedPolylines = mergedPolylines,
			UnmergedIndices = unmergedIndicesList
		};
	}

	private static List<EndpointConnection> FindConnections(IReadOnlyList<Polyline> polylines, double tolerance)
	{
		List<EndpointConnection> endpointConnections = new List<EndpointConnection>();
		double toleranceSq = tolerance * tolerance;
		for (int i = 0; i < polylines.Count; i++)
		{
			Polyline polyline = polylines[i];
			if (polyline == null || polyline.PointCount < 2)
			{
				continue;
			}
			Point ptA = polyline.First();
			Point ptA2 = polyline.Last();
			for (int j = i + 1; j < polylines.Count; j++)
			{
				Polyline polyline2 = polylines[j];
				if (polyline2 != null && polyline2.PointCount >= 2)
				{
					Point ptB = polyline2.First();
					Point ptB2 = polyline2.Last();
					TryAddConnection(endpointConnections, i, isStartA: true, j, isStartB: true, ptA, ptB, toleranceSq);
					TryAddConnection(endpointConnections, i, isStartA: true, j, isStartB: false, ptA, ptB2, toleranceSq);
					TryAddConnection(endpointConnections, i, isStartA: false, j, isStartB: true, ptA2, ptB, toleranceSq);
					TryAddConnection(endpointConnections, i, isStartA: false, j, isStartB: false, ptA2, ptB2, toleranceSq);
				}
			}
		}
		return endpointConnections;
	}

	private static void TryAddConnection(List<EndpointConnection> connections, int idxA, bool isStartA, int idxB, bool isStartB, Point ptA, Point ptB, double toleranceSq)
	{
		double distanceSquaredTo = ptA.DistanceSquaredTo(ptB);
		if (!(distanceSquaredTo > toleranceSq))
		{
			connections.Add(new EndpointConnection
			{
				IndexA = idxA,
				IsStartA = isStartA,
				IndexB = idxB,
				IsStartB = isStartB,
				Distance = Math.Sqrt(distanceSquaredTo)
			});
		}
	}

	private static bool TryBuildChain(IReadOnlyList<Polyline> polylines, HashSet<int> groupIndices, List<EndpointConnection> connections, MergeMode mode, out Polyline chain, out List<int> mergedIndices)
	{
		chain = null;
		mergedIndices = new List<int>();
		Dictionary<int, List<EndpointConnection>> endpointConnectionListByInt = new Dictionary<int, List<EndpointConnection>>();
		Dictionary<int, List<EndpointConnection>> endpointConnectionListByInt2 = new Dictionary<int, List<EndpointConnection>>();
		foreach (int groupIndex in groupIndices)
		{
			endpointConnectionListByInt[groupIndex] = new List<EndpointConnection>();
			endpointConnectionListByInt2[groupIndex] = new List<EndpointConnection>();
		}
		foreach (EndpointConnection connection in connections)
		{
			if (groupIndices.Contains(connection.IndexA) && groupIndices.Contains(connection.IndexB))
			{
				if (connection.IsStartA)
				{
					endpointConnectionListByInt[connection.IndexA].Add(connection);
				}
				else
				{
					endpointConnectionListByInt2[connection.IndexA].Add(connection);
				}
				EndpointConnection endpointConnection2 = new EndpointConnection
				{
					IndexA = connection.IndexB,
					IsStartA = connection.IsStartB,
					IndexB = connection.IndexA,
					IsStartB = connection.IsStartA,
					Distance = connection.Distance
				};
				if (connection.IsStartB)
				{
					endpointConnectionListByInt[connection.IndexB].Add(endpointConnection2);
				}
				else
				{
					endpointConnectionListByInt2[connection.IndexB].Add(endpointConnection2);
				}
			}
		}
		if (mode == MergeMode.RejectBranching)
		{
			foreach (int groupIndex2 in groupIndices)
			{
				if (endpointConnectionListByInt[groupIndex2].Count > 1 || endpointConnectionListByInt2[groupIndex2].Count > 1)
				{
					return false;
				}
			}
		}
		int unity = -1;
		bool condition = true;
		foreach (int groupIndex3 in groupIndices)
		{
			if (endpointConnectionListByInt[groupIndex3].Count == 0)
			{
				unity = groupIndex3;
				condition = true;
				break;
			}
			if (endpointConnectionListByInt2[groupIndex3].Count == 0)
			{
				unity = groupIndex3;
				condition = false;
				break;
			}
		}
		if (unity < 0)
		{
			unity = groupIndices.First();
			condition = true;
		}
		List<Polyline> polylinesList = new List<Polyline>();
		HashSet<int> intSet = new HashSet<int>();
		int indexBEndpointConnectionTerm = unity;
		bool isStartBEndpointConnectionTerm = condition;
		while (!intSet.Contains(indexBEndpointConnectionTerm))
		{
			intSet.Add(indexBEndpointConnectionTerm);
			mergedIndices.Add(indexBEndpointConnectionTerm);
			Polyline polyline = polylines[indexBEndpointConnectionTerm].Clone();
			if (!isStartBEndpointConnectionTerm)
			{
				polyline.Reverse();
			}
			polylinesList.Add(polyline);
			List<EndpointConnection> endpointConnections = ((!isStartBEndpointConnectionTerm) ? endpointConnectionListByInt[indexBEndpointConnectionTerm] : endpointConnectionListByInt2[indexBEndpointConnectionTerm]);
			if (endpointConnections.Count == 0)
			{
				break;
			}
			EndpointConnection endpointConnection = PickBestConnection(endpointConnections, intSet, mode);
			if (!endpointConnection.IsValid)
			{
				break;
			}
			indexBEndpointConnectionTerm = endpointConnection.IndexB;
			isStartBEndpointConnectionTerm = endpointConnection.IsStartB;
		}
		if (mode == MergeMode.RejectBranching && mergedIndices.Count != groupIndices.Count)
		{
			return false;
		}
		if (polylinesList.Count < 2)
		{
			return false;
		}
		chain = Polyline.Concatenate(polylinesList, double.MaxValue);
		return true;
	}

	private static EndpointConnection PickBestConnection(List<EndpointConnection> connections, HashSet<int> visited, MergeMode mode)
	{
		List<EndpointConnection> endpointConnections = new List<EndpointConnection>();
		foreach (EndpointConnection connection in connections)
		{
			if (!visited.Contains(connection.IndexB))
			{
				endpointConnections.Add(connection);
			}
		}
		if (endpointConnections.Count == 0)
		{
			return EndpointConnection.None;
		}
		if (endpointConnections.Count == 1)
		{
			return endpointConnections[0];
		}
		EndpointConnection result = endpointConnections[0];
		foreach (EndpointConnection endpointConnection in endpointConnections)
		{
			if (endpointConnection.Distance < result.Distance)
			{
				result = endpointConnection;
			}
		}
		return result;
	}
}
