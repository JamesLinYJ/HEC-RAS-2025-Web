using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Utility.Systems;

namespace RasMapperLib.TinHelpers;

public struct TriangleStruct
{
	public int p0;

	public int p1;

	public int p2;

	public int at0;

	public int at1;

	public int at2;

	public byte EdgeType;

	// C# has no syntax for parameterized property 'AdjacentTriangleExists'.
	public bool get_AdjacentTriangleExists(int e012)
	{
		return e012 switch
		{
			0 => at0 >= 0, 
			1 => at1 >= 0, 
			_ => at2 >= 0, 
		};
	}

	// C# has no syntax for parameterized property 'AdjacentTriangle'.
	public int get_AdjacentTriangle(int e012)
	{
		return e012 switch
		{
			0 => at0, 
			1 => at1, 
			_ => at2, 
		};
	}

	// C# has no syntax for parameterized property 'AdjacentTriangle'.
	public int get_AdjacentTriangle(int firstpoint, int secondPoint)
	{
		if (((p0 == firstpoint) & (p1 == secondPoint)) || ((p1 == firstpoint) & (p0 == secondPoint)))
		{
			return at0;
		}
		if (((p1 == firstpoint) & (p2 == secondPoint)) || ((p2 == firstpoint) & (p1 == secondPoint)))
		{
			return at1;
		}
		return at2;
	}

	public void set_AdjacentTriangle(int firstpoint, int secondPoint, int intValue)
	{
		if (((p0 == firstpoint) & (p1 == secondPoint)) || ((p1 == firstpoint) & (p0 == secondPoint)))
		{
			at0 = intValue;
		}
		else if (((p1 == firstpoint) & (p2 == secondPoint)) || ((p2 == firstpoint) & (p1 == secondPoint)))
		{
			at1 = intValue;
		}
		else
		{
			at2 = intValue;
		}
	}

	public void AssignPoints(int pt0, int pt1, int pt2)
	{
		p0 = pt0;
		p1 = pt1;
		p2 = pt2;
		at0 = -1;
		at1 = -1;
		at2 = -1;
	}

	public int GetEdge(int pt0, int pt1)
	{
		if (((pt0 == p0) & (pt1 == p1)) || ((pt0 == p1) & (pt1 == p0)))
		{
			return 0;
		}
		if (((pt0 == p1) & (pt1 == p2)) || ((pt0 == p2) & (pt1 == p1)))
		{
			return 1;
		}
		return 2;
	}

	public int EdgePoint0(int e012)
	{
		return e012 switch
		{
			0 => p0, 
			1 => p1, 
			_ => p2, 
		};
	}

	public int EdgePoint1(int e012)
	{
		return e012 switch
		{
			0 => p1, 
			1 => p2, 
			_ => p0, 
		};
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

	public int ThirdPoint(int firstPoint, int secondPoint)
	{
		if (p0 != firstPoint && p0 != secondPoint)
		{
			return p0;
		}
		if (p1 != firstPoint && p1 != secondPoint)
		{
			return p1;
		}
		return p2;
	}

	public void MinMidMaxV(double v0, double v1, double v2, ref int min, ref int mid, ref int max)
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

	public PlaneM PlaneCoeffients(Tin TIN)
	{
		PointM pointM = TIN.PointM(p0);
		PointM ptM = TIN.PointM(p1);
		PointM ptM2 = TIN.PointM(p2);
		PointM pointM2 = pointM.Delta(ptM);
		PointM pointM3 = pointM.Delta(ptM2);
		PlaneM result = default;
		result.A = pointM2.Y * pointM3.Z - pointM2.Z * pointM3.Y;
		result.B = pointM2.Z * pointM3.X - pointM2.X * pointM3.Z;
		result.C = pointM2.X * pointM3.Y - pointM2.Y * pointM3.X;
		result.D = (0.0 - result.A) * pointM.X - result.B * pointM.Y - result.C * pointM.Z;
		if (!double.IsNaN(pointM.M))
		{
			result.E = pointM2.Y * pointM3.M - pointM2.M * pointM3.Y;
			result.F = pointM2.M * pointM3.X - pointM2.X * pointM3.M;
			result.G = result.C;
			result.H = (0.0 - result.E) * pointM.X - result.F * pointM.Y - result.G * pointM.M;
		}
		return result;
	}

	public bool IsEdge0Breakline()
	{
		return (EdgeType & 1) > 0;
	}

	public bool IsEdge1Breakline()
	{
		return (EdgeType & 2) > 0;
	}

	public bool IsEdge2Breakline()
	{
		return (EdgeType & 4) > 0;
	}

	public bool IsEdge0Perimeter()
	{
		return (EdgeType & 8) > 0;
	}

	public bool IsEdge1Perimeter()
	{
		return (EdgeType & 0x10) > 0;
	}

	public bool IsEdge2Perimeter()
	{
		return (EdgeType & 0x20) > 0;
	}

	public static void Stream(BinaryWriter bw, IList<TriangleStruct> tris)
	{
		checked
		{
			int[] intValues = new int[tris.Count * 7 - 1 + 1];
			int count = tris.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				TriangleStruct triangleStruct = tris[i];
				intValues[i * 7] = triangleStruct.p0;
				intValues[i * 7 + 1] = triangleStruct.p1;
				intValues[i * 7 + 2] = triangleStruct.p2;
				intValues[i * 7 + 3] = triangleStruct.at0;
				intValues[i * 7 + 4] = triangleStruct.at1;
				intValues[i * 7 + 5] = triangleStruct.at2;
				intValues[i * 7 + 6] = triangleStruct.EdgeType;
			}
			Pipe.WriteArray(bw, intValues);
		}
	}

	public static TriangleStruct[] Stream(BinaryReader br)
	{
		int[] readArray = Pipe.ReadArray<int>(br);
		if (readArray.Count() == 0)
		{
			return new TriangleStruct[0];
		}
		checked
		{
			TriangleStruct[] triangleStructs = new TriangleStruct[(int)Math.Round((double)readArray.Count() / 7.0) - 1 + 1];
			int count = triangleStructs.Count() - 1;
			TriangleStruct triangleStruct = default;
			for (int i = 0; i <= count; i++)
			{
				triangleStruct.p0 = readArray[i * 7];
				triangleStruct.p1 = readArray[i * 7 + 1];
				triangleStruct.p2 = readArray[i * 7 + 2];
				triangleStruct.at0 = readArray[i * 7 + 3];
				triangleStruct.at1 = readArray[i * 7 + 4];
				triangleStruct.at2 = readArray[i * 7 + 5];
				triangleStruct.EdgeType = (byte)readArray[i * 7 + 6];
				triangleStructs[i] = triangleStruct;
			}
			return triangleStructs;
		}
	}
}
