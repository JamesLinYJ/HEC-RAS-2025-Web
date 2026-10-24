using System;
using System.Collections.Generic;
using Geospatial.Vectors.Topology;
using Utility.Memory;
using Utility.Systems;

namespace Geospatial.Vectors.Graph;

public class Graph : BaseTopology<GraphArc>, IStreamableObject, ISpatialVersion
{
	protected override GraphArc BuildArc(int startNode, int endNode, List<Point> internalVertices)
	{
		return new GraphArc(startNode, endNode, internalVertices);
	}

	public override void TryFixInvalidStates()
	{
		base.TryFixInvalidStates();
		RebuildNodeToArcPointers();
	}

	public IEnumerable<int> OutgoingArcsAsDAG(int nodeId)
	{
		Node node = _nodes[nodeId];
		for (int i = 0; i < node.Arcs.Count; i++)
		{
			NodeArcConnection nodeArcConnection = node.Arcs[i];
			if (nodeArcConnection.IsStart)
			{
				yield return nodeArcConnection.ArcID;
			}
		}
	}

	public IEnumerable<int> IncomingArcsAsDAG(int nodeId)
	{
		Node node = _nodes[nodeId];
		for (int i = 0; i < node.Arcs.Count; i++)
		{
			NodeArcConnection nodeArcConnection = node.Arcs[i];
			if (!nodeArcConnection.IsStart)
			{
				yield return nodeArcConnection.ArcID;
			}
		}
	}

	public bool HasCycleAsDAG()
	{
		int[] cycleArcIds;
		return HasCycleAsDAG(out cycleArcIds);
	}

	public bool HasCycleAsDAG(out int[] cycleArcIds)
	{
		cycleArcIds = Array.Empty<int>();
		int count = _nodes.Count;
		if (count == 0)
		{
			return false;
		}
		byte[] byteValues = new byte[count];
		int[] intValues = new int[count];
		int[] intValuesArray = new int[count];
		for (int i = 0; i < count; i++)
		{
			intValues[i] = -1;
			intValuesArray[i] = -1;
		}
		int[][] intValues2 = new int[count][];
		int[][] intValues3 = new int[count][];
		BuildDagAdjacency(intValues2, intValues3);
		Stack<(int, int)> valueTuples = new Stack<(int, int)>();
		for (int j = 0; j < count; j++)
		{
			if (byteValues[j] != 0)
			{
				continue;
			}
			byteValues[j] = 1;
			valueTuples.Push((j, 0));
			while (valueTuples.Count > 0)
			{
				(int, int) tuple = valueTuples.Pop();
				int tupleTerm = tuple.Item1;
				int tupleTerm2 = tuple.Item2;
				int[] intValue = intValues2[tupleTerm];
				int[] intValueArray = intValues3[tupleTerm];
				if (tupleTerm2 >= intValue.Length)
				{
					byteValues[tupleTerm] = 2;
					continue;
				}
				int ordinal = intValue[tupleTerm2];
				int ordinal2 = intValueArray[tupleTerm2];
				valueTuples.Push((tupleTerm, tupleTerm2 + 1));
				if (byteValues[ordinal] == 1)
				{
					cycleArcIds = ReconstructCycle(ordinal, tupleTerm, ordinal2, intValues, intValuesArray);
					return true;
				}
				if (byteValues[ordinal] == 0)
				{
					byteValues[ordinal] = 1;
					intValues[ordinal] = tupleTerm;
					intValuesArray[ordinal] = ordinal2;
					valueTuples.Push((ordinal, 0));
				}
			}
		}
		return false;
	}

	private void BuildDagAdjacency(int[][] outAdjNodes, int[][] outAdjArcs)
	{
		int count = _nodes.Count;
		int[] intValues = new int[count];
		for (int i = 0; i < _arcs.Count; i++)
		{
			GraphArc graphArc = _arcs[i];
			if (graphArc.StartNode != -1 && graphArc.EndNode != -1)
			{
				intValues[graphArc.StartNode]++;
			}
		}
		for (int j = 0; j < count; j++)
		{
			outAdjNodes[j] = new int[intValues[j]];
			outAdjArcs[j] = new int[intValues[j]];
			intValues[j] = 0;
		}
		for (int k = 0; k < _arcs.Count; k++)
		{
			GraphArc graphArc2 = _arcs[k];
			if (graphArc2.StartNode != -1 && graphArc2.EndNode != -1)
			{
				int startNode = graphArc2.StartNode;
				outAdjNodes[startNode][intValues[startNode]] = graphArc2.EndNode;
				outAdjArcs[startNode][intValues[startNode]] = k;
				intValues[startNode]++;
			}
		}
	}

	private static int[] ReconstructCycle(int cycleStart, int backEdgeFrom, int backEdgeArc, int[] parentNode, int[] parentArc)
	{
		List<int> intList = new List<int>();
		int backEdgeFrom2 = backEdgeFrom;
		while (backEdgeFrom2 != cycleStart && backEdgeFrom2 != -1)
		{
			intList.Add(parentArc[backEdgeFrom2]);
			backEdgeFrom2 = parentNode[backEdgeFrom2];
		}
		intList.Reverse();
		intList.Add(backEdgeArc);
		return intList.ToArray();
	}

	public bool TryTopologicalSortAsDAG(out int[] nodeOrder)
	{
		int count = _nodes.Count;
		int[] intValues = new int[count];
		for (int i = 0; i < _arcs.Count; i++)
		{
			GraphArc graphArc = _arcs[i];
			if (graphArc.StartNode != -1 && graphArc.EndNode != -1)
			{
				intValues[graphArc.EndNode]++;
			}
		}
		Queue<int> intQueue = new Queue<int>();
		for (int j = 0; j < count; j++)
		{
			if (intValues[j] == 0)
			{
				intQueue.Enqueue(j);
			}
		}
		List<int> intList = new List<int>(count);
		while (intQueue.Count > 0)
		{
			int dequeue = intQueue.Dequeue();
			intList.Add(dequeue);
			foreach (int outgoingArcsAsDAG in OutgoingArcsAsDAG(dequeue))
			{
				int endNode = _arcs[outgoingArcsAsDAG].EndNode;
				if (endNode != -1 && --intValues[endNode] == 0)
				{
					intQueue.Enqueue(endNode);
				}
			}
		}
		nodeOrder = intList.ToArray();
		return intList.Count == count;
	}

	public bool IsEmpty()
	{
		if (Arcs.Count == 0)
		{
			return Nodes.Count == 0;
		}
		return false;
	}

	public Graph Clone(Graph cloneInto = null)
	{
		if (cloneInto == null)
		{
			cloneInto = new Graph();
		}
		cloneInto.ArcMetadata.ClearRows();
		cloneInto.NodeMetadata.ClearRows();
		cloneInto._nodes.Clear();
		cloneInto._arcs.Clear();
		for (int i = 0; i < Nodes.Count; i++)
		{
			cloneInto._nodes.Add(Nodes[i].Clone());
		}
		cloneInto._nodeVersion++;
		for (int j = 0; j < Arcs.Count; j++)
		{
			cloneInto._arcs.Add(Arcs[j].Clone());
		}
		cloneInto.NodeMetadata = CloneTable(NodeMetadata);
		cloneInto.ArcMetadata = CloneTable(ArcMetadata);
		cloneInto.GlobalMetadata = CloneTable(GlobalMetadata);
		long versionXY = cloneInto.VersionXY;
		cloneInto._globalVersion += Math.Max(VersionXY, versionXY) + 1 - versionXY;
		return cloneInto;
		static Table CloneTable(Table tbl)
		{
			Table table = tbl.CloneStructure(copyNewRowEvent: false);
			for (int k = 0; k < tbl.Rows.Count; k++)
			{
				TableRow tableRow = tbl.Rows[k];
				TableRow tableRow2 = table.AddNewRow();
				tableRow.ItemArray.CopyTo(tableRow2.ItemArray, 0);
			}
			return table;
		}
	}
}
