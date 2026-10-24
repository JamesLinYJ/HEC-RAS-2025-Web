using System;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{p0},{p1},{p2}")]
public struct TriangleDefinition
{
	public readonly int p0;

	public readonly int p1;

	public readonly int p2;

	public int at0;

	public int at1;

	public int at2;

	private bool _isDeleted;

	public bool IsDeleted
	{
		get
		{
			return _isDeleted;
		}
		set
		{
			_isDeleted = value;
		}
	}

	public static TriangleDefinition NullTriangleDef()
	{
		return new TriangleDefinition(-1, -1, -1, -1, -1, -1, isDeleted: true);
	}

	public bool IsNull()
	{
		if (p0 == -1 && p1 == -1 && p2 == -1 && at0 == -1 && at1 == -1)
		{
			return at2 == -1;
		}
		return false;
	}

	public TriangleDefinition(int p0, int p1, int p2, int at0, int at1, int at2, bool isDeleted = false)
	{
		this.p0 = p0;
		this.p1 = p1;
		this.p2 = p2;
		this.at0 = at0;
		this.at1 = at1;
		this.at2 = at2;
		_isDeleted = isDeleted;
	}

	public void ChangeAdjacentTriangle(int fromIdx, int toIdx)
	{
		if (at0 == fromIdx)
		{
			at0 = toIdx;
			return;
		}
		if (at1 == fromIdx)
		{
			at1 = toIdx;
			return;
		}
		if (at2 == fromIdx)
		{
			at2 = toIdx;
			return;
		}
		throw new ArgumentException("Triangle " + fromIdx + " does not exist as an adjacent triangle.");
	}

	public int AdjacentTriangleIdx(int at012)
	{
		return at012 switch
		{
			0 => at0, 
			1 => at1, 
			2 => at2, 
			_ => throw new ArgumentException("at012"), 
		};
	}

	public int AdjacentTriangleByGlobalIdx(int tIdx)
	{
		if (tIdx == at0)
		{
			return 0;
		}
		if (tIdx == at1)
		{
			return 1;
		}
		if (tIdx == at2)
		{
			return 2;
		}
		throw new ArgumentException("tIdx");
	}

	public int AdjacentPointIdx(int pt012)
	{
		return pt012 switch
		{
			0 => p0, 
			1 => p1, 
			2 => p2, 
			_ => throw new ArgumentException("pt012"), 
		};
	}

	public int AdjacentTriangleIdx(int ptAIdx, int ptBIdx)
	{
		if ((p0 == ptAIdx && p1 == ptBIdx) || (p1 == ptAIdx && p0 == ptBIdx))
		{
			return at0;
		}
		if ((p1 == ptAIdx && p2 == ptBIdx) || (p2 == ptAIdx && p1 == ptBIdx))
		{
			return at1;
		}
		return at2;
	}

	public void SetAdjacentTriangle(int ptAIdx, int ptBIdx, int adjTriIdx)
	{
		if ((p0 == ptAIdx && p1 == ptBIdx) || (p1 == ptAIdx && p0 == ptBIdx))
		{
			at0 = adjTriIdx;
		}
		else if ((p1 == ptAIdx && p2 == ptBIdx) || (p2 == ptAIdx && p1 == ptBIdx))
		{
			at1 = adjTriIdx;
		}
		else
		{
			at2 = adjTriIdx;
		}
	}

	public int OppositeE012FromPoint(int ptIdx)
	{
		if (ptIdx == p0)
		{
			return 1;
		}
		if (ptIdx == p1)
		{
			return 2;
		}
		if (ptIdx == p2)
		{
			return 0;
		}
		throw new Exception("Bad Point index parameter");
	}

	public int AdjacentTriangleOppositePoint_GlobalIndex(int ptIdx)
	{
		if (ptIdx == p0)
		{
			return at1;
		}
		if (ptIdx == p1)
		{
			return at2;
		}
		if (ptIdx == p2)
		{
			return at0;
		}
		throw new Exception("Bad Point index parameter");
	}

	public int GetClockwisePointWithinTriangle(int ptIdx)
	{
		if (ptIdx == p0)
		{
			return p2;
		}
		if (ptIdx == p1)
		{
			return p0;
		}
		if (ptIdx == p2)
		{
			return p1;
		}
		throw new ArgumentException("Point " + ptIdx + " does not belong to the triangle.");
	}

	public int GetCounterClockwisePointWithinTriangle(int ptIdx)
	{
		if (ptIdx == p0)
		{
			return p1;
		}
		if (ptIdx == p1)
		{
			return p2;
		}
		if (ptIdx == p2)
		{
			return p0;
		}
		throw new ArgumentException("Point " + ptIdx + " does not belong to the triangle.");
	}

	public int GetCounterClockwiseTriangleRotatingWithin(int aroundPointIdx)
	{
		if (p0 == aroundPointIdx)
		{
			return at0;
		}
		if (p1 == aroundPointIdx)
		{
			return at1;
		}
		if (p2 == aroundPointIdx)
		{
			return at2;
		}
		throw new Exception("Logic error rotating around triangle.");
	}

	public int GetClockwiseTriangleRotatingWithin(int aroundPointIdx)
	{
		if (p0 == aroundPointIdx)
		{
			return at2;
		}
		if (p1 == aroundPointIdx)
		{
			return at0;
		}
		if (p2 == aroundPointIdx)
		{
			return at1;
		}
		throw new Exception("Logic error rotating around triangle.");
	}

	public int GetCounterClockwiseTriangleAroundPoint(int ptIdx)
	{
		return GetClockwiseTriangleRotatingWithin(ptIdx);
	}

	public int GetClockwiseTriangleAroundPoint(int ptIdx)
	{
		return GetCounterClockwiseTriangleRotatingWithin(ptIdx);
	}

	public int GetClockwiseE012(int e012)
	{
		return (e012 + 2) % 3;
	}

	public int GetCounterClockwiseE012(int e012)
	{
		return (e012 + 1) % 3;
	}

	public int GetEdge012(int ptA, int ptB)
	{
		if ((ptA == p0 && ptB == p1) || (ptA == p1 && ptB == p0))
		{
			return 0;
		}
		if ((ptA == p1 && ptB == p2) || (ptA == p2 && ptB == p1))
		{
			return 1;
		}
		return 2;
	}

	public int TryGetEdge012(int ptA, int ptB)
	{
		if ((ptA == p0 && ptB == p1) || (ptA == p1 && ptB == p0))
		{
			return 0;
		}
		if ((ptA == p1 && ptB == p2) || (ptA == p2 && ptB == p1))
		{
			return 1;
		}
		if ((ptA == p2 && ptB == p0) || (ptA == p0 && ptB == p2))
		{
			return 2;
		}
		return -1;
	}

	public int GetEdge012(int adjTriIdx)
	{
		if (at0 == adjTriIdx)
		{
			return 0;
		}
		if (at1 == adjTriIdx)
		{
			return 1;
		}
		return 2;
	}

	public int EdgePointA(int e012)
	{
		return e012 switch
		{
			0 => p0, 
			1 => p1, 
			_ => p2, 
		};
	}

	public int EdgePointB(int e012)
	{
		return e012 switch
		{
			0 => p1, 
			1 => p2, 
			_ => p0, 
		};
	}

	public int EdgePointOpposite(int e012)
	{
		return e012 switch
		{
			0 => p2, 
			1 => p0, 
			_ => p1, 
		};
	}

	public void EdgePoints(int e012, out int ptA, out int ptB)
	{
		ptA = EdgePointA(e012);
		ptB = EdgePointB(e012);
	}

	public int Point(int p012)
	{
		return p012 switch
		{
			0 => p0, 
			1 => p1, 
			_ => p2, 
		};
	}

	public int Point012ByGlobalIdx(int pIdx)
	{
		if (p0 == pIdx)
		{
			return 0;
		}
		if (p1 == pIdx)
		{
			return 1;
		}
		if (p2 == pIdx)
		{
			return 2;
		}
		throw new ArgumentException("Point does not belong to triangle.");
	}

	public int ThirdPoint(int pA, int pB)
	{
		if (p0 != pA && p0 != pB)
		{
			return p0;
		}
		if (p1 != pA && p1 != pB)
		{
			return p1;
		}
		return p2;
	}

	public void MinMidMaxV(double v0, double v1, double v2, out int min, out int mid, out int max)
	{
		if (v0 <= v1 && v1 <= v2)
		{
			min = p0;
			mid = p1;
			max = p2;
		}
		else if (v0 <= v2 && v2 <= v1)
		{
			min = p0;
			mid = p2;
			max = p1;
		}
		else if (v1 <= v0 && v0 <= v2)
		{
			min = p1;
			mid = p0;
			max = p2;
		}
		else if (v1 <= v2 && v2 <= v0)
		{
			min = p1;
			mid = p2;
			max = p0;
		}
		else if (v2 <= v0 && v0 <= v1)
		{
			min = p2;
			mid = p0;
			max = p1;
		}
		else
		{
			min = p2;
			mid = p1;
			max = p0;
		}
	}

	public bool IsSuperTriangle(int superPtCount)
	{
		if (p0 >= superPtCount && p1 >= superPtCount)
		{
			return p2 < superPtCount;
		}
		return true;
	}
}
