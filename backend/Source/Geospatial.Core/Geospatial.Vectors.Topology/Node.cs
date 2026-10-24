using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Utility.Hashing;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

[DebuggerDisplay("{DebugDisplay()}")]
public class Node : IDataHash, IStreamableObject
{
	public const int NullNodeFID = -1;

	public Point Point;

	public List<NodeArcConnection> Arcs = new List<NodeArcConnection>();

	public Node()
	{
	}

	public Node(Point pt)
	{
		Point = pt;
	}

	public string DebugDisplay()
	{
		string arcsTerm = Point.ToString() + ": A[";
		for (int i = 0; i < Arcs.Count; i++)
		{
			arcsTerm = arcsTerm + Arcs[i].ToString() + ",";
		}
		return arcsTerm + "]";
	}

	public int GetDataHash()
	{
		int dataHash = Point.GetHashCode();
		for (int i = 0; i < Arcs.Count; i++)
		{
			dataHash = HashCode.Combine(dataHash, Arcs[i]);
		}
		return dataHash;
	}

	public Node Clone()
	{
		return new Node
		{
			Point = Point,
			Arcs = new List<NodeArcConnection>(Arcs)
		};
	}

	public bool Equals(Node otherNode)
	{
		if (!Point.EqualXY(in otherNode.Point))
		{
			return false;
		}
		if (Arcs.Count != otherNode.Arcs.Count)
		{
			return false;
		}
		for (int i = 0; i < Arcs.Count; i++)
		{
			if (!Arcs.Contains(otherNode.Arcs[i]))
			{
				return false;
			}
		}
		return true;
	}

	public void StreamOut(BinaryWriter bw)
	{
		Point.StreamOut(bw);
		bw.StreamCollectionOut(Arcs);
	}

	public void StreamIn(BinaryReader br)
	{
		Point.StreamIn(br);
		Arcs.Clear();
		Arcs.AddRange(br.StreamCollectionIn<NodeArcConnection>());
	}
}
