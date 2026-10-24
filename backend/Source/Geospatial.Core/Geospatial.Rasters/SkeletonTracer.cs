using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public static class SkeletonTracer
{
	private struct GraphNode(double row, double col, bool isEndpoint)
	{
		public double Row = row;

		public double Col = col;

		public bool IsEndpoint = isEndpoint;
	}

	private class GraphEdge
	{
		public int StartNode;

		public int EndNode;

		public List<(int r, int c)> Path;

		public bool Removed;

		public GraphEdge(int startNode, int endNode, List<(int, int)> path)
		{
			StartNode = startNode;
			EndNode = endNode;
			Path = path;
		}
	}

	private static readonly int[] DRow = new int[8] { -1, -1, 0, 1, 1, 1, 0, -1 };

	private static readonly int[] DCol = new int[8] { 0, 1, 1, 1, 0, -1, -1, -1 };

	private const byte Endpoint = 1;

	private const byte Chain = 2;

	private const byte Junction = 3;

	public static PolylineCollection Trace(ReadOnlySpan<byte> skeleton, int width, int height, RasterDefinition rasterDef, int minLengthPixels, double simplifyTolerance)
	{
		List<(int, int)> valueTuples = new List<(int, int)>();
		for (int i = 0; i < skeleton.Length; i++)
		{
			if (skeleton[i] != 0)
			{
				valueTuples.Add((i / width, i % width));
			}
		}
		return Trace(valueTuples, width, height, rasterDef, minLengthPixels, simplifyTolerance);
	}

	public static PolylineCollection Trace(List<(int r, int c)> skeletonPixels, int width, int height, RasterDefinition rasterDef, int minLengthPixels, double simplifyTolerance)
	{
		HashSet<long> longs = new HashSet<long>(skeletonPixels.Count);
		foreach (var (r, c) in skeletonPixels)
		{
			longs.Add(Key(r, c, width));
		}
		Dictionary<long, byte> byteByLong = new Dictionary<long, byte>(skeletonPixels.Count);
		List<(int, int)> valueTuples = new List<(int, int)>();
		List<(int, int)> valueTuplesList = new List<(int, int)>();
		List<(int, int)> valueTuples2 = new List<(int, int)>();
		foreach (var skeletonPixel in skeletonPixels)
		{
			int skeletonPixelTerm = skeletonPixel.r;
			int skeletonPixelTerm2 = skeletonPixel.c;
			int countNeighbors = CountNeighbors(skeletonPixelTerm, skeletonPixelTerm2, width, height, longs);
			if (countNeighbors != 0)
			{
				long key = Key(skeletonPixelTerm, skeletonPixelTerm2, width);
				switch (countNeighbors)
				{
				case 1:
					byteByLong[key] = 1;
					valueTuples.Add((skeletonPixelTerm, skeletonPixelTerm2));
					break;
				case 2:
					byteByLong[key] = 2;
					valueTuples2.Add((skeletonPixelTerm, skeletonPixelTerm2));
					break;
				default:
					byteByLong[key] = 3;
					valueTuplesList.Add((skeletonPixelTerm, skeletonPixelTerm2));
					break;
				}
			}
		}
		Dictionary<long, int> intByLong = new Dictionary<long, int>();
		List<List<(int, int)>> valueTupleListList = new List<List<(int, int)>>();
		Queue<(int, int)> valueTuples3 = new Queue<(int, int)>();
		foreach (var localValue in valueTuplesList)
		{
			int ordinal = localValue.Item1;
			int ordinal2 = localValue.Item2;
			long key2 = Key(ordinal, ordinal2, width);
			if (intByLong.ContainsKey(key2))
			{
				continue;
			}
			List<(int, int)> valueTuples4 = new List<(int, int)>();
			int countOffset = valueTupleListList.Count + 1;
			valueTuples3.Enqueue((ordinal, ordinal2));
			intByLong[key2] = countOffset;
			while (valueTuples3.Count > 0)
			{
				var (localValue2, localValue3) = valueTuples3.Dequeue();
				valueTuples4.Add((localValue2, localValue3));
				for (int i = 0; i < 8; i++)
				{
					int dRowTerm = localValue2 + DRow[i];
					int dColTerm = localValue3 + DCol[i];
					if (dRowTerm >= 0 && dRowTerm < height && dColTerm >= 0 && dColTerm < width)
					{
						long key3 = Key(dRowTerm, dColTerm, width);
						if (byteByLong.TryGetValue(key3, out var localValue4) && localValue4 == 3 && !intByLong.ContainsKey(key3))
						{
							intByLong[key3] = countOffset;
							valueTuples3.Enqueue((dRowTerm, dColTerm));
						}
					}
				}
			}
			valueTupleListList.Add(valueTuples4);
		}
		List<GraphNode> graphNodes = new List<GraphNode>();
		List<GraphEdge> graphEdges = new List<GraphEdge>();
		Dictionary<long, int> intByLong2 = new Dictionary<long, int>();
		foreach (var localValue5 in valueTuples)
		{
			int ordinal3 = localValue5.Item1;
			int ordinal4 = localValue5.Item2;
			long key4 = Key(ordinal3, ordinal4, width);
			intByLong2[key4] = graphNodes.Count;
			graphNodes.Add(new GraphNode(ordinal3, ordinal4, isEndpoint: true));
		}
		int[] intValues = new int[valueTupleListList.Count];
		for (int j = 0; j < valueTupleListList.Count; j++)
		{
			List<(int, int)> valueTupleListListList = valueTupleListList[j];
			double computed = 0.0;
			double computed2 = 0.0;
			foreach (var localValue6 in valueTupleListListList)
			{
				int ordinal5 = localValue6.Item1;
				int ordinal6 = localValue6.Item2;
				computed += (double)ordinal5;
				computed2 += (double)ordinal6;
			}
			intValues[j] = graphNodes.Count;
			graphNodes.Add(new GraphNode(computed / (double)valueTupleListListList.Count, computed2 / (double)valueTupleListListList.Count, isEndpoint: false));
			foreach (var (r2, c2) in valueTupleListListList)
			{
				intByLong2[Key(r2, c2, width)] = graphNodes.Count - 1;
			}
		}
		HashSet<long> longsSet = new HashSet<long>();
		for (int k = 0; k < graphNodes.Count; k++)
		{
			GraphNode graphNode = graphNodes[k];
			List<(int, int)> valueTuples5;
			if (graphNode.IsEndpoint)
			{
				valueTuples5 = new List<(int, int)> { ((int)graphNode.Row, (int)graphNode.Col) };
			}
			else
			{
				int graphNodesTerm = k - (graphNodes.Count - valueTupleListList.Count);
				if (graphNodesTerm < 0 || graphNodesTerm >= valueTupleListList.Count)
				{
					continue;
				}
				valueTuples5 = valueTupleListList[graphNodesTerm];
			}
			foreach (var localValue7 in valueTuples5)
			{
				int ordinal7 = localValue7.Item1;
				int ordinal8 = localValue7.Item2;
				for (int l = 0; l < 8; l++)
				{
					int dRowTerm2 = ordinal7 + DRow[l];
					int dColTerm2 = ordinal8 + DCol[l];
					if (dRowTerm2 < 0 || dRowTerm2 >= height || dColTerm2 < 0 || dColTerm2 >= width)
					{
						continue;
					}
					long widthTerm = Key(dRowTerm2, dColTerm2, width);
					if (!longs.Contains(widthTerm) || longsSet.Contains(widthTerm) || (intByLong2.TryGetValue(widthTerm, out var localValue8) && localValue8 == k))
					{
						continue;
					}
					if (byteByLong.TryGetValue(widthTerm, out var localValue9) && (localValue9 == 1 || localValue9 == 3))
					{
						int ordinal9 = intByLong2[widthTerm];
						if (ordinal9 >= k)
						{
							graphEdges.Add(new GraphEdge(k, ordinal9, new List<(int, int)>
							{
								(ordinal7, ordinal8),
								(dRowTerm2, dColTerm2)
							}));
							longsSet.Add(widthTerm);
						}
						continue;
					}
					List<(int, int)> valueTuples6 = new List<(int, int)> { (ordinal7, ordinal8) };
					int ordinal10 = dRowTerm2;
					int ordinal11 = dColTerm2;
					longsSet.Add(widthTerm);
					valueTuples6.Add((ordinal10, ordinal11));
					int ordinal12 = -1;
					while (true)
					{
						int ordinal13 = -1;
						int ordinal14 = -1;
						bool condition = false;
						for (int m = 0; m < 8; m++)
						{
							int dRowTerm3 = ordinal10 + DRow[m];
							int dColTerm3 = ordinal11 + DCol[m];
							if (dRowTerm3 < 0 || dRowTerm3 >= height || dColTerm3 < 0 || dColTerm3 >= width)
							{
								continue;
							}
							long widthTerm2 = Key(dRowTerm3, dColTerm3, width);
							if (!longs.Contains(widthTerm2))
							{
								continue;
							}
							if (byteByLong.TryGetValue(widthTerm2, out var localValue10) && (localValue10 == 1 || localValue10 == 3))
							{
								int ordinal15 = intByLong2[widthTerm2];
								if (ordinal15 != k || valueTuples6.Count > 2)
								{
									valueTuples6.Add((dRowTerm3, dColTerm3));
									ordinal12 = ordinal15;
									condition = true;
									break;
								}
							}
							else if (!longsSet.Contains(widthTerm2))
							{
								ordinal13 = dRowTerm3;
								ordinal14 = dColTerm3;
							}
						}
						if (condition || ordinal13 < 0)
						{
							break;
						}
						ordinal10 = ordinal13;
						ordinal11 = ordinal14;
						longsSet.Add(Key(ordinal10, ordinal11, width));
						valueTuples6.Add((ordinal10, ordinal11));
					}
					if (ordinal12 >= 0)
					{
						graphEdges.Add(new GraphEdge(k, ordinal12, valueTuples6));
					}
					else if (valueTuples6.Count >= 2)
					{
						graphEdges.Add(new GraphEdge(k, -1, valueTuples6));
					}
				}
			}
		}
		foreach (var localValue11 in valueTuples2)
		{
			int ordinal16 = localValue11.Item1;
			int ordinal17 = localValue11.Item2;
			long widthTerm3 = Key(ordinal16, ordinal17, width);
			if (longsSet.Contains(widthTerm3))
			{
				continue;
			}
			List<(int, int)> valueTuples7 = new List<(int, int)>();
			int ordinal18 = ordinal16;
			int ordinal19 = ordinal17;
			longsSet.Add(widthTerm3);
			valueTuples7.Add((ordinal18, ordinal19));
			while (true)
			{
				int ordinal20 = -1;
				int ordinal21 = -1;
				for (int n = 0; n < 8; n++)
				{
					int dRowTerm4 = ordinal18 + DRow[n];
					int dColTerm4 = ordinal19 + DCol[n];
					if (dRowTerm4 >= 0 && dRowTerm4 < height && dColTerm4 >= 0 && dColTerm4 < width)
					{
						long widthTerm4 = Key(dRowTerm4, dColTerm4, width);
						if (longs.Contains(widthTerm4) && !longsSet.Contains(widthTerm4))
						{
							ordinal20 = dRowTerm4;
							ordinal21 = dColTerm4;
							break;
						}
					}
				}
				if (ordinal20 < 0)
				{
					break;
				}
				ordinal18 = ordinal20;
				ordinal19 = ordinal21;
				longsSet.Add(Key(ordinal18, ordinal19, width));
				valueTuples7.Add((ordinal18, ordinal19));
			}
			if (valueTuples7.Count >= 3)
			{
				valueTuples7.Add(valueTuples7[0]);
			}
			if (valueTuples7.Count >= 2)
			{
				graphEdges.Add(new GraphEdge(-1, -1, valueTuples7));
			}
		}
		PruneShortBranches(graphNodes, graphEdges, minLengthPixels);
		PolylineCollection polylineCollection = new PolylineCollection();
		double cellSize = rasterDef.CellSize;
		double minX = rasterDef.MinX;
		double maxY = rasterDef.MaxY;
		foreach (GraphEdge graphEdge in graphEdges)
		{
			if (graphEdge.Removed || graphEdge.Path.Count < 2)
			{
				continue;
			}
			List<Point> points = new List<Point>(graphEdge.Path.Count);
			foreach (var path in graphEdge.Path)
			{
				int pathTerm = path.r;
				int pathTerm2 = path.c;
				double x = minX + ((double)pathTerm2 + 0.5) * cellSize;
				double y = maxY - ((double)pathTerm + 0.5) * cellSize;
				points.Add(new Point(x, y));
			}
			Polyline polyline = new Polyline(points);
			if (simplifyTolerance > 0.0)
			{
				polyline.Filter(simplifyTolerance);
			}
			if (polyline.PointCount >= 2)
			{
				polylineCollection.Add(polyline);
			}
		}
		return polylineCollection;
	}

	private static long Key(int r, int c, int width)
	{
		return (long)r * (long)width + c;
	}

	private static int CountNeighbors(int r, int c, int w, int h, HashSet<long> skeletonSet)
	{
		int ordinal = 0;
		for (int i = 0; i < 8; i++)
		{
			int dRowTerm = r + DRow[i];
			int dColTerm = c + DCol[i];
			if (dRowTerm >= 0 && dRowTerm < h && dColTerm >= 0 && dColTerm < w && skeletonSet.Contains(Key(dRowTerm, dColTerm, w)))
			{
				ordinal++;
			}
		}
		return ordinal;
	}

	private static void PruneShortBranches(List<GraphNode> nodes, List<GraphEdge> edges, int minLengthPixels)
	{
		if (minLengthPixels <= 0)
		{
			return;
		}
		for (int i = 0; i < 10; i++)
		{
			bool condition = false;
			int[] intValues = new int[nodes.Count];
			for (int j = 0; j < edges.Count; j++)
			{
				GraphEdge graphEdge = edges[j];
				if (!graphEdge.Removed)
				{
					if (graphEdge.StartNode >= 0)
					{
						intValues[graphEdge.StartNode]++;
					}
					if (graphEdge.EndNode >= 0)
					{
						intValues[graphEdge.EndNode]++;
					}
				}
			}
			for (int k = 0; k < edges.Count; k++)
			{
				GraphEdge graphEdge2 = edges[k];
				if (graphEdge2.Removed || graphEdge2.Path.Count >= minLengthPixels)
				{
					continue;
				}
				bool startNodeIsNegative = graphEdge2.StartNode < 0 || intValues[graphEdge2.StartNode] == 1;
				bool endNodeIsNegative = graphEdge2.EndNode < 0 || intValues[graphEdge2.EndNode] == 1;
				if (startNodeIsNegative | endNodeIsNegative)
				{
					graphEdge2.Removed = true;
					if (graphEdge2.StartNode >= 0)
					{
						intValues[graphEdge2.StartNode]--;
					}
					if (graphEdge2.EndNode >= 0)
					{
						intValues[graphEdge2.EndNode]--;
					}
					condition = true;
				}
			}
			for (int l = 0; l < nodes.Count; l++)
			{
				if (intValues[l] != 2)
				{
					continue;
				}
				int ordinal = -1;
				int ordinal2 = -1;
				for (int m = 0; m < edges.Count; m++)
				{
					GraphEdge graphEdge3 = edges[m];
					if (!graphEdge3.Removed && (graphEdge3.StartNode == l || graphEdge3.EndNode == l))
					{
						if (ordinal >= 0)
						{
							ordinal2 = m;
							break;
						}
						ordinal = m;
					}
				}
				if (ordinal >= 0 && ordinal2 >= 0)
				{
					GraphEdge graphEdge4 = edges[ordinal];
					GraphEdge graphEdge5 = edges[ordinal2];
					List<(int, int)> path = new List<(int, int)>(graphEdge4.Path);
					if (graphEdge4.StartNode == l)
					{
						path.Reverse();
					}
					List<(int, int)> pathList = new List<(int, int)>(graphEdge5.Path);
					if (graphEdge5.EndNode == l)
					{
						pathList.Reverse();
					}
					for (int n = 1; n < pathList.Count; n++)
					{
						path.Add(pathList[n]);
					}
					int startNode = ((graphEdge4.StartNode == l) ? graphEdge4.EndNode : graphEdge4.StartNode);
					int endNode = ((graphEdge5.StartNode == l) ? graphEdge5.EndNode : graphEdge5.StartNode);
					graphEdge4.Path = path;
					graphEdge4.StartNode = startNode;
					graphEdge4.EndNode = endNode;
					graphEdge5.Removed = true;
				}
			}
			for (int intervalIndex = 0; intervalIndex < nodes.Count; intervalIndex++)
			{
				if (intValues[intervalIndex] == 1 && !nodes[intervalIndex].IsEndpoint)
				{
					nodes[intervalIndex] = new GraphNode(nodes[intervalIndex].Row, nodes[intervalIndex].Col, isEndpoint: true);
				}
			}
			if (!condition)
			{
				break;
			}
		}
	}
}
