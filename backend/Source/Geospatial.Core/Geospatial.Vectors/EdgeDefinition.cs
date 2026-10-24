using System;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{ToDebuggerDisplay()}")]
public struct EdgeDefinition
{
	public int ATriangleIndex;

	public int BTriangleIndex;

	public int AEdge012;

	public int BEdge012;

	public int APointIndex;

	public int BPointIndex;

	public bool IsExternal
	{
		get
		{
			if (ATriangleIndex != -1)
			{
				return BTriangleIndex == -1;
			}
			return true;
		}
	}

	public static EdgeDefinition NullEdgeDefinition()
	{
		return new EdgeDefinition(-1, -1, -1, -1, -1, -1);
	}

	public static bool IsNull(EdgeDefinition ed)
	{
		return ed.IsNull();
	}

	public bool IsNull()
	{
		if (ATriangleIndex == -1 && BTriangleIndex == -1 && AEdge012 == -1 && BEdge012 == -1 && APointIndex == -1)
		{
			return BPointIndex == -1;
		}
		return false;
	}

	public EdgeDefinition(int aTriIdx, int e012A, int bTriIdx, int e012B, int aPointIdx, int bPointIdx)
	{
		ATriangleIndex = aTriIdx;
		BTriangleIndex = bTriIdx;
		AEdge012 = e012A;
		BEdge012 = e012B;
		APointIndex = aPointIdx;
		BPointIndex = bPointIdx;
	}

	public int OtherPoint(int ptIdx)
	{
		if (APointIndex == ptIdx)
		{
			return BPointIndex;
		}
		if (BPointIndex == ptIdx)
		{
			return APointIndex;
		}
		throw new ArgumentException(ptIdx + " doesn't belong to this edge.");
	}

	public int OtherTriangle(int tIdx)
	{
		if (ATriangleIndex == tIdx)
		{
			return BTriangleIndex;
		}
		if (BTriangleIndex == tIdx)
		{
			return ATriangleIndex;
		}
		throw new ArgumentException(tIdx + " doesn't belong to this edge.");
	}

	public int EitherDefinedTriangleIndex()
	{
		if (ATriangleIndex == -1 && BTriangleIndex == -1)
		{
			return -1;
		}
		if (ATriangleIndex == -1)
		{
			return BTriangleIndex;
		}
		return ATriangleIndex;
	}

	public override bool Equals(object obj)
	{
		if (obj is EdgeDefinition edgeDefinition)
		{
			return edgeDefinition == this;
		}
		return false;
	}

	public override int GetHashCode()
	{
		if (ATriangleIndex < BTriangleIndex)
		{
			return HashCode.Combine(ATriangleIndex, BTriangleIndex, AEdge012, BEdge012, APointIndex, BPointIndex);
		}
		return HashCode.Combine(BTriangleIndex, ATriangleIndex, BEdge012, AEdge012, BPointIndex, APointIndex);
	}

	public static bool operator ==(EdgeDefinition edge1, EdgeDefinition edge2)
	{
		if (edge1.ATriangleIndex == edge2.ATriangleIndex && edge1.AEdge012 == edge2.AEdge012 && edge1.BTriangleIndex == edge2.BTriangleIndex && edge1.BEdge012 == edge2.BEdge012)
		{
			return true;
		}
		if (edge1.ATriangleIndex == edge2.BTriangleIndex && edge1.AEdge012 == edge2.BEdge012 && edge1.BTriangleIndex == edge2.ATriangleIndex && edge1.BEdge012 == edge2.AEdge012)
		{
			return true;
		}
		return false;
	}

	public static bool operator !=(EdgeDefinition eA, EdgeDefinition eB)
	{
		return !(eA == eB);
	}

	public string ToDebuggerDisplay()
	{
		return "Point " + APointIndex + " to " + BPointIndex;
	}

	public EdgeDefinition Flip()
	{
		return new EdgeDefinition(BTriangleIndex, BEdge012, ATriangleIndex, AEdge012, BPointIndex, APointIndex);
	}
}
