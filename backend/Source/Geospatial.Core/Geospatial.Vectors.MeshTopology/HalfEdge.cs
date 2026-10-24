using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Geospatial.Vectors.MeshTopology;

[DebuggerDisplay("{DebugString()}")]
public struct HalfEdge(int fIdx, byte orient) : IEquatable<HalfEdge>
{
	[JsonInclude]
	public int fIdx = fIdx;

	[JsonInclude]
	public byte orient = orient;

	public static readonly HalfEdge NullHalfEdge = new HalfEdge(-100, 0);

	public static bool IsNull(HalfEdge he)
	{
		return he.fIdx == NullHalfEdge.fIdx;
	}

	public bool IsDirected()
	{
		if (orient != 0)
		{
			return orient == 1;
		}
		return true;
	}

	public HalfEdge Undirected()
	{
		return new HalfEdge(fIdx, 2);
	}

	public bool IsNull()
	{
		return IsNull(this);
	}

	public override bool Equals(object obj)
	{
		if (obj is HalfEdge he)
		{
			return Equals(he);
		}
		return false;
	}

	public bool Equals(HalfEdge he)
	{
		if (fIdx == he.fIdx)
		{
			return orient == he.orient;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return (fIdx, orient).GetHashCode();
	}

	public static bool operator ==(HalfEdge lhs, HalfEdge rhs)
	{
		return lhs.Equals(rhs);
	}

	public static bool operator !=(HalfEdge lhs, HalfEdge rhs)
	{
		return !(lhs == rhs);
	}

	public string DebugString()
	{
		return "[f:" + fIdx + " dir:" + orient + "]";
	}

	public override string ToString()
	{
		return DebugString();
	}

	public HalfEdge Flipped()
	{
		return new HalfEdge(fIdx, (byte)(1 - orient));
	}
}
