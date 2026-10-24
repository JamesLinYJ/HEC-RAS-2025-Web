using System;
using System.Collections.Generic;

namespace RasMapperLib.TinHelpers;

public class DevelopmentTriangle
{
	public int p0;

	public int p1;

	public int p2;

	public int at0;

	public int at1;

	public int at2;

	private byte Flags;

	public byte GetFlags()
	{
		return Flags;
	}

	public DevelopmentTriangle(int p0, int p1, int p2, int at0, int at1, int at2)
	{
		this.p0 = p0;
		this.p1 = p1;
		this.p2 = p2;
		this.at0 = at0;
		this.at1 = at1;
		this.at2 = at2;
		Flags = 0;
	}

	public void ChangeAT(int sourceVal, int destVal)
	{
		if (at0 == sourceVal)
		{
			at0 = destVal;
			return;
		}
		if (at1 == sourceVal)
		{
			at1 = destVal;
			return;
		}
		if (at2 == sourceVal)
		{
			at2 = destVal;
			return;
		}
		throw new ArgumentException("Triangle " + sourceVal + " does not exist as an AT triangle.");
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

	public bool IsDeleted()
	{
		return (Flags & 0x40) != 0;
	}

	public void MarkDeleted()
	{
		Flags |= 64;
	}

	public void SetEdge(int e012, bool isPerimeter)
	{
		switch (e012)
		{
		case 0:
			if (isPerimeter)
			{
				Flags |= 8;
			}
			else
			{
				Flags |= 1;
			}
			break;
		case 1:
			if (isPerimeter)
			{
				Flags |= 16;
			}
			else
			{
				Flags |= 2;
			}
			break;
		default:
			if (isPerimeter)
			{
				Flags |= 32;
			}
			else
			{
				Flags |= 4;
			}
			break;
		}
	}

	public bool IsAnyEdgePerimOrBreakline()
	{
		return (Flags & 0x3F) != 0;
	}

	public bool IsEdge0Breakline()
	{
		return (Flags & 1) != 0;
	}

	public bool IsEdge1Breakline()
	{
		return (Flags & 2) != 0;
	}

	public bool IsEdge2Breakline()
	{
		return (Flags & 4) != 0;
	}

	public bool IsEdge0Perimeter()
	{
		return (Flags & 8) != 0;
	}

	public bool IsEdge1Perimeter()
	{
		return (Flags & 0x10) != 0;
	}

	public bool IsEdge2Perimeter()
	{
		return (Flags & 0x20) != 0;
	}

	public int GetCWTriangle(int aroundPointIdx, ref bool isEdgeBLorPerim)
	{
		if (p0 == aroundPointIdx)
		{
			isEdgeBLorPerim = IsEdge0Breakline() || IsEdge0Perimeter();
			return at0;
		}
		if (p1 == aroundPointIdx)
		{
			isEdgeBLorPerim = IsEdge1Breakline() || IsEdge1Perimeter();
			return at1;
		}
		if (p2 == aroundPointIdx)
		{
			isEdgeBLorPerim = IsEdge2Breakline() || IsEdge2Perimeter();
			return at2;
		}
		return -1;
	}

	public int GetCCWTriangle(int aroundPointIdx, ref bool isEdgeBLorPerim)
	{
		if (p0 == aroundPointIdx)
		{
			isEdgeBLorPerim = IsEdge2Breakline() || IsEdge2Perimeter();
			return at2;
		}
		if (p1 == aroundPointIdx)
		{
			isEdgeBLorPerim = IsEdge0Breakline() || IsEdge0Perimeter();
			return at0;
		}
		if (p2 == aroundPointIdx)
		{
			isEdgeBLorPerim = IsEdge1Breakline() || IsEdge1Perimeter();
			return at1;
		}
		return -1;
	}

	public CircleM CircumCircleCompute(List<PointM> pts)
	{
		CircleM result = default;
		result.Compute(pts[p0], pts[p1], pts[p2]);
		return result;
	}

	public static CircleM CircumCircleCompute(PointM p0, PointM p1, PointM p2)
	{
		CircleM result = default;
		result.Compute(p0, p1, p2);
		return result;
	}
}
