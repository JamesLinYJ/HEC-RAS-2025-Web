using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class DevelopmentTin
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__43_002D0
	{
		public float[] floatValues;

		public bool cellsSet;

		public _Closure_0024__43_002D0(_Closure_0024__43_002D0 other)
		{
			if (other != null)
			{
				floatValues = other.floatValues;
				cellsSet = other.cellsSet;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n, float z)
		{
			floatValues[n] = z;
			cellsSet = true;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__44_002D0
	{
		public bool[] mask;

		public bool cellsSet;

		public _Closure_0024__44_002D0(_Closure_0024__44_002D0 other)
		{
			if (other != null)
			{
				mask = other.mask;
				cellsSet = other.cellsSet;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			mask[n] = true;
			cellsSet = true;
		}
	}

	private List<DevelopmentTriangle> _triangles;

	private PointMs _points;

	private Extent _extent;

	[SpecialName]
	private int _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;

	[SpecialName]
	private StaticLocalInitFlag _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init;

	[SpecialName]
	private double[] _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd;

	[SpecialName]
	private StaticLocalInitFlag _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init;

	public List<DevelopmentTriangle> Triangles => _triangles;

	public PointMs Points => _points;

	public Extent Extent => _extent;

	public DevelopmentTin(List<DevelopmentTriangle> triangles, PointMs points, Extent extent)
	{
		_triangles = triangles;
		_points = points;
		_extent = extent;
	}

	public DevelopmentTin(List<DevelopmentTriangle> triangles, PointMs points)
		: this(triangles, points, points.ComputeExtent())
	{
	}

	public void RemoveDeletedTriangles()
	{
		int ordinal = 0;
		checked
		{
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				if (developmentTriangle.IsDeleted())
				{
					if (developmentTriangle.at0 != -1)
					{
						_triangles[developmentTriangle.at0].ChangeAT(i, -1);
					}
					if (developmentTriangle.at1 != -1)
					{
						_triangles[developmentTriangle.at1].ChangeAT(i, -1);
					}
					if (developmentTriangle.at2 != -1)
					{
						_triangles[developmentTriangle.at2].ChangeAT(i, -1);
					}
					continue;
				}
				if (developmentTriangle.at0 != -1)
				{
					_triangles[developmentTriangle.at0].ChangeAT(i, ordinal);
				}
				if (developmentTriangle.at1 != -1)
				{
					_triangles[developmentTriangle.at1].ChangeAT(i, ordinal);
				}
				if (developmentTriangle.at2 != -1)
				{
					_triangles[developmentTriangle.at2].ChangeAT(i, ordinal);
				}
				_triangles[ordinal] = developmentTriangle;
				ordinal++;
			}
			int trianglesTerm = _triangles.Count - ordinal;
			if (trianglesTerm > 0)
			{
				_triangles.RemoveRange(ordinal, trianglesTerm);
			}
			SqueezePoints();
			if (_points.Count != 0)
			{
				_extent = _points.ComputeExtent();
			}
			else
			{
				_extent = null;
			}
		}
	}

	private void SqueezePoints()
	{
		int pointCount = 0;
		bool[] usedPointArray = GetUsedPointArray();
		int[] remappedPoints = GetRemappedPoints(usedPointArray, ref pointCount);
		checked
		{
			int count = _points.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int remappedPoint = remappedPoints[i];
				if (remappedPoint != -1)
				{
					_points[remappedPoint] = _points[i];
				}
			}
			_points.RemoveRange(pointCount);
			int count2 = _triangles.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[j];
				developmentTriangle.p0 = remappedPoints[developmentTriangle.p0];
				developmentTriangle.p1 = remappedPoints[developmentTriangle.p1];
				developmentTriangle.p2 = remappedPoints[developmentTriangle.p2];
			}
		}
	}

	public bool ContainsPoint(PointM point)
	{
		if (!_extent.Contains(point))
		{
			return false;
		}
		int triangleContainingPoint = FindTriangleContainingPoint(point);
		if (triangleContainingPoint == -1)
		{
			return false;
		}
		return !_triangles[triangleContainingPoint].IsDeleted();
	}

	public bool ContainsPointThreadsafe(PointM point, ref int triIdx)
	{
		if (!_extent.Contains(point))
		{
			return false;
		}
		FindTriangleContainingPointThreadsafe(point, ref triIdx);
		if (triIdx == -1)
		{
			return false;
		}
		return !_triangles[triIdx].IsDeleted();
	}

	public bool ContainsPointBufferedFavorOutOfBounds(PointM point, double distanceTolerance)
	{
		if (!_extent.Contains(point))
		{
			return false;
		}
		int triangleContainingPoint = FindTriangleContainingPoint(point);
		if (triangleContainingPoint == -1)
		{
			return false;
		}
		DevelopmentTriangle developmentTriangle = _triangles[triangleContainingPoint];
		if (developmentTriangle.IsDeleted())
		{
			return false;
		}
		int ordinal = 0;
		checked
		{
			do
			{
				int point2 = developmentTriangle.AdjacentPointIdx(ordinal);
				foreach (int trianglesAroundPoint in TrianglesAroundPoint(point2, triangleContainingPoint))
				{
					if (!_triangles[trianglesAroundPoint].IsDeleted())
					{
						continue;
					}
					int ordinal2 = 0;
					do
					{
						SegmentM segmentM = Edge(trianglesAroundPoint, ordinal2);
						SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
						if (segmentM.DistanceToPoint(point, ref relativePosition) < distanceTolerance)
						{
							return false;
						}
						ordinal2++;
					}
					while (ordinal2 <= 2);
				}
				ordinal++;
			}
			while (ordinal <= 2);
			return true;
		}
	}

	public bool ContainsPointBufferedFavorInBounds(PointM point, double distanceTolerance)
	{
		if (!_extent.Extend(distanceTolerance).Contains(point))
		{
			return false;
		}
		int triangleContainingPoint = FindTriangleContainingPoint(point);
		if (triangleContainingPoint == -1)
		{
			return false;
		}
		DevelopmentTriangle developmentTriangle = _triangles[triangleContainingPoint];
		if (!developmentTriangle.IsDeleted())
		{
			return true;
		}
		int ordinal = 0;
		checked
		{
			do
			{
				int point2 = developmentTriangle.AdjacentPointIdx(ordinal);
				foreach (int trianglesAroundPoint in TrianglesAroundPoint(point2, triangleContainingPoint))
				{
					if (_triangles[trianglesAroundPoint].IsDeleted())
					{
						continue;
					}
					int ordinal2 = 0;
					do
					{
						SegmentM segmentM = Edge(trianglesAroundPoint, ordinal2);
						SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
						if (segmentM.DistanceToPoint(point, ref relativePosition) < distanceTolerance)
						{
							return true;
						}
						ordinal2++;
					}
					while (ordinal2 <= 2);
				}
				ordinal++;
			}
			while (ordinal <= 2);
			return false;
		}
	}

	public List<Tuple<int, int, int>> TriEdgeTri(int point, int tri)
	{
		List<int> trianglesAroundPoint = TrianglesAroundPoint(point, tri);
		List<Tuple<int, int, int>> tupleList = new List<Tuple<int, int, int>>();
		checked
		{
			int count = trianglesAroundPoint.Count - 1;
			DevelopmentTriangle tri2;
			int cW_EdgePoint;
			for (int i = 0; i <= count; i++)
			{
				tri2 = _triangles[trianglesAroundPoint[i]];
				cW_EdgePoint = GetCW_EdgePoint(tri2, point);
				int cW_EdgeTriangle = GetCW_EdgeTriangle(tri2, point);
				int trianglesAroundPoint2 = trianglesAroundPoint[i];
				tupleList.Add(new Tuple<int, int, int>(cW_EdgeTriangle, cW_EdgePoint, trianglesAroundPoint2));
			}
			tri2 = _triangles[trianglesAroundPoint.Last()];
			cW_EdgePoint = GetCCW_EdgePoint(tri2, point);
			if (cW_EdgePoint != tupleList[0].Item2)
			{
				int cW_EdgeTriangle = trianglesAroundPoint.Last();
				int ccW_EdgeTriangle = GetCCW_EdgeTriangle(tri2, point);
				tupleList.Add(new Tuple<int, int, int>(cW_EdgeTriangle, cW_EdgePoint, ccW_EdgeTriangle));
			}
			return tupleList;
		}
	}

	public List<int> TrianglesAroundPoint(int point, int startingTriangle)
	{
		List<int> intList = new List<int>();
		int startingTriangle2 = startingTriangle;
		do
		{
			intList.Add(startingTriangle2);
			DevelopmentTriangle tri = _triangles[startingTriangle2];
			startingTriangle2 = GetCCW_EdgeTriangle(tri, point);
		}
		while (startingTriangle2 != startingTriangle && startingTriangle2 != -1);
		if (startingTriangle2 == startingTriangle)
		{
			return intList;
		}
		startingTriangle2 = startingTriangle;
		intList.RemoveAt(0);
		do
		{
			intList.Insert(0, startingTriangle2);
			DevelopmentTriangle tri = _triangles[startingTriangle2];
			startingTriangle2 = GetCW_EdgeTriangle(tri, point);
		}
		while (startingTriangle2 != -1);
		return intList;
	}

	public List<int> PointsAroundPoint(int point, int startingTriangle)
	{
		List<int> trianglesAroundPoint = TrianglesAroundPoint(point, startingTriangle);
		List<int> intList = new List<int>();
		checked
		{
			int count = Triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				intList.Add(GetCW_EdgePoint(_triangles[trianglesAroundPoint[i]], point));
			}
			intList.Add(GetCCW_EdgePoint(_triangles[trianglesAroundPoint.Last()], point));
			return intList;
		}
	}

	private int GetCW_EdgePoint(DevelopmentTriangle tri, int point)
	{
		if (point == tri.p0)
		{
			return tri.p1;
		}
		if (point == tri.p1)
		{
			return tri.p2;
		}
		if (point == tri.p2)
		{
			return tri.p0;
		}
		throw new ArgumentException("Point " + point + " does not belong to the triangle.");
	}

	private int GetCCW_EdgePoint(DevelopmentTriangle tri, int point)
	{
		if (point == tri.p0)
		{
			return tri.p2;
		}
		if (point == tri.p1)
		{
			return tri.p0;
		}
		if (point == tri.p2)
		{
			return tri.p1;
		}
		throw new ArgumentException("Point " + point + " does not belong to the triangle.");
	}

	private int GetCCW_EdgeTriangle(DevelopmentTriangle tri, int point)
	{
		if (point == tri.p0)
		{
			return tri.at2;
		}
		if (point == tri.p1)
		{
			return tri.at0;
		}
		if (point == tri.p2)
		{
			return tri.at1;
		}
		throw new ArgumentException("Point " + point + " does not belong to the triangle.");
	}

	private int GetCW_EdgeTriangle(DevelopmentTriangle tri, int point)
	{
		if (point == tri.p0)
		{
			return tri.at0;
		}
		if (point == tri.p1)
		{
			return tri.at1;
		}
		if (point == tri.p2)
		{
			return tri.at2;
		}
		throw new ArgumentException("Point " + point + " does not belong to the triangle.");
	}

	public void PointTriangles(int p, int[] point2TriangleMap, List<int> triangles, ref bool pIsExternal)
	{
		triangles.Clear();
		int point2TriangleMap2 = point2TriangleMap[p];
		if (point2TriangleMap2 > -1)
		{
			triangles.Add(point2TriangleMap2);
			int point2TriangleMap3 = point2TriangleMap2;
			do
			{
				point2TriangleMap3 = TriangleAdjacentToEdgeBeforePoint(point2TriangleMap3, p);
				if ((point2TriangleMap3 != point2TriangleMap2) & (point2TriangleMap3 != -1))
				{
					triangles.Add(point2TriangleMap3);
				}
			}
			while (!((point2TriangleMap3 == point2TriangleMap2) | (point2TriangleMap3 == -1)));
			if (point2TriangleMap3 == -1)
			{
				pIsExternal = true;
				point2TriangleMap3 = point2TriangleMap2;
				do
				{
					point2TriangleMap3 = TriangleAdjacentToEdgeAfterPoint(point2TriangleMap3, p);
					if (point2TriangleMap3 != -1)
					{
						triangles.Insert(0, point2TriangleMap3);
					}
				}
				while (point2TriangleMap3 != -1);
			}
			else
			{
				pIsExternal = false;
			}
		}
		else
		{
			pIsExternal = true;
		}
	}

	public int[] CreatePoint2TriangleMap()
	{
		checked
		{
			int[] point2TriangleMap = new int[Points.Count - 1 + 1];
			int count = Points.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				point2TriangleMap[i] = -1;
			}
			int count2 = Triangles.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				point2TriangleMap[Triangles[j].p0] = j;
				point2TriangleMap[Triangles[j].p1] = j;
				point2TriangleMap[Triangles[j].p2] = j;
			}
			return point2TriangleMap;
		}
	}

	public void TriangleAdjacentIndexes(int t, ref int at0, ref int at1, ref int at2)
	{
		at0 = Triangles[t].at0;
		at1 = Triangles[t].at1;
		at2 = Triangles[t].at2;
	}

	public int TriangleAdjacentToEdgeBeforePoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].at2;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].at0;
		}
		return Triangles[t].at1;
	}

	public int TriangleAdjacentToEdgeAfterPoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].at0;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].at1;
		}
		return Triangles[t].at2;
	}

	public int TrianglePointBeforePoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].p2;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].p0;
		}
		return Triangles[t].p1;
	}

	public int TrianglePointAfterPoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].p1;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].p2;
		}
		return Triangles[t].p0;
	}

	public int FindTriangleContainingPoint(PointM point)
	{
		if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init == null)
		{
			Interlocked.CompareExchange(ref _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init, new StaticLocalInitFlag(), null);
		}
		bool lockTaken = false;
		try
		{
			Monitor.Enter(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init, ref lockTaken);
			if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State == 0)
			{
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State = 2;
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t = 0;
			}
			else if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State == 2)
			{
				throw new IncompleteInitialization();
			}
		}
		finally
		{
			_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init.State = 1;
			if (lockTaken)
			{
				Monitor.Exit(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t_0024Init);
			}
		}
		checked
		{
			if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t < 0 || _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t > _triangles.Count - 1)
			{
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t = 0;
			}
			int ordinal = 0;
			int ordinal2 = -1;
			int adjacentTriangleIdxDevelopmentTriangleTerm = -1;
			if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init == null)
			{
				Interlocked.CompareExchange(ref _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init, new StaticLocalInitFlag(), null);
			}
			bool lockTaken2 = false;
			try
			{
				Monitor.Enter(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init, ref lockTaken2);
				if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State == 0)
				{
					_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State = 2;
					_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd = new double[3];
				}
				else if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State == 2)
				{
					throw new IncompleteInitialization();
				}
			}
			finally
			{
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init.State = 1;
				if (lockTaken2)
				{
					Monitor.Exit(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd_0024Init);
				}
			}
			while (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t != -1)
			{
				DevelopmentTriangle developmentTriangle = _triangles[_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t];
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[0] = CrossProduct(_points[developmentTriangle.p0], _points[developmentTriangle.p1], point);
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[1] = CrossProduct(_points[developmentTriangle.p1], _points[developmentTriangle.p2], point);
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[2] = CrossProduct(_points[developmentTriangle.p2], _points[developmentTriangle.p0], point);
				int min3Index = RasMapperLib.Utilities.Math.Min3Index(_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[0], _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[1], _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[2]);
				if (_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024crossProd[min3Index] >= 0.0)
				{
					return _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;
				}
				adjacentTriangleIdxDevelopmentTriangleTerm = developmentTriangle.AdjacentTriangleIdx(min3Index);
				if (ordinal2 == adjacentTriangleIdxDevelopmentTriangleTerm)
				{
					return _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;
				}
				ordinal2 = _0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t;
				_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t = adjacentTriangleIdxDevelopmentTriangleTerm;
				ordinal++;
				if ((_0024STATIC_0024FindTriangleContainingPoint_002420181180F0_0024t == -1) | (ordinal == _triangles.Count))
				{
					break;
				}
			}
			_ = _triangles.Count;
			return -1;
		}
	}

	public void FindTriangleContainingPointThreadsafe(PointM point, ref int t)
	{
		checked
		{
			if (t < 0 || t > _triangles.Count - 1)
			{
				t = 0;
			}
			int ordinal = 0;
			int ordinal2 = -1;
			int adjacentTriangleIdxDevelopmentTriangleTerm = -1;
			while (true)
			{
				if (t != -1)
				{
					DevelopmentTriangle developmentTriangle = _triangles[t];
					double d = CrossProduct(_points[developmentTriangle.p0], _points[developmentTriangle.p1], point);
					double d2 = CrossProduct(_points[developmentTriangle.p1], _points[developmentTriangle.p2], point);
					double d3 = CrossProduct(_points[developmentTriangle.p2], _points[developmentTriangle.p0], point);
					int idx = 0;
					double computed = 0.0;
					RasMapperLib.Utilities.Math.Min3Index(d, d2, d3, ref idx, ref computed);
					if (computed >= 0.0)
					{
						break;
					}
					adjacentTriangleIdxDevelopmentTriangleTerm = developmentTriangle.AdjacentTriangleIdx(idx);
					if (ordinal2 == adjacentTriangleIdxDevelopmentTriangleTerm)
					{
						break;
					}
					ordinal2 = t;
					t = adjacentTriangleIdxDevelopmentTriangleTerm;
					ordinal++;
					if (!((t == -1) | (ordinal == _triangles.Count)))
					{
						continue;
					}
				}
				t = -1;
				break;
			}
		}
	}

	public SegmentM Edge(int t, int e012)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		SegmentM result = default;
		switch (e012)
		{
		case 0:
			result.A = p;
			result.B = p2;
			break;
		case 1:
			result.A = p2;
			result.B = p3;
			break;
		default:
			result.A = p3;
			result.B = p;
			break;
		}
		return result;
	}

	public void MarkDeleted(List<int> delTris)
	{
		foreach (int delTri in delTris)
		{
			_triangles[delTri].MarkDeleted();
		}
		RemoveDeletedTriangles();
	}

	public void Validate()
	{
		checked
		{
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				if (developmentTriangle.at0 != -1)
				{
					_triangles[developmentTriangle.at0].ChangeAT(i, i);
				}
				if (developmentTriangle.at1 != -1)
				{
					_triangles[developmentTriangle.at1].ChangeAT(i, i);
				}
				if (developmentTriangle.at2 != -1)
				{
					_triangles[developmentTriangle.at2].ChangeAT(i, i);
				}
			}
		}
	}

	public PointM TriangleCentroid(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return TriangleCentroid(p, p2, p3);
	}

	public static PointM TriangleCentroid(PointM pt0, PointM pt1, PointM pt2)
	{
		SegmentM segmentM = new SegmentM(pt0, pt1.MidPoint(pt2));
		SegmentM seg = new SegmentM(pt1, pt0.MidPoint(pt2));
		PointM result = default;
		segmentM.Intersects(seg, ref result.X, ref result.Y);
		return result;
	}

	public PointM TriangleCircumcircle(int t)
	{
		CircleM circleM = _triangles[t].CircumCircleCompute(_points);
		return new PointM(circleM.X, circleM.Y);
	}

	public Tin MakeCleanTin()
	{
		int triangleCount = 0;
		int[] triangleKeepMap = MakeTriangleKeepMap(ref triangleCount);
		bool[] usedPointArray = GetUsedPointArray();
		int pointCount = 0;
		int[] remappedPoints = GetRemappedPoints(usedPointArray, ref pointCount);
		PointMs finalPoints = GetFinalPoints(pointCount, usedPointArray);
		TriangleStruct[] finalTriangles = GetFinalTriangles(triangleCount, remappedPoints, triangleKeepMap);
		return new Tin(finalPoints, finalTriangles);
	}

	public Tin MakeTinWithDeletedTris()
	{
		checked
		{
			TriangleStruct[] triangleStructs = new TriangleStruct[_triangles.Count - 1 + 1];
			int count = _triangles.Count - 1;
			TriangleStruct triangleStruct = default;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				triangleStruct.p0 = developmentTriangle.p0;
				triangleStruct.p1 = developmentTriangle.p1;
				triangleStruct.p2 = developmentTriangle.p2;
				triangleStruct.at0 = developmentTriangle.at0;
				triangleStruct.at0 = developmentTriangle.at1;
				triangleStruct.at0 = developmentTriangle.at2;
				triangleStruct.EdgeType = developmentTriangle.GetFlags();
				triangleStructs[i] = triangleStruct;
			}
			return new Tin(_points, triangleStructs);
		}
	}

	public void ExportToPolygonFeatureLayer(PolygonFeatureLayer pfl)
	{
		string columnName = "Status";
		pfl.AddAttributeColumn(columnName, typeof(string));
		checked
		{
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				PointMs pointMs = new PointMs(3);
				pointMs.Add(_points[developmentTriangle.p0]);
				pointMs.Add(_points[developmentTriangle.p1]);
				pointMs.Add(_points[developmentTriangle.p2]);
				pfl.AddFeature(new Polygon(pointMs));
				if (developmentTriangle.IsDeleted())
				{
					pfl.FeatureRow(pfl.FeatureCount() - 1)[columnName] = "Deleted";
				}
				else
				{
					pfl.FeatureRow(pfl.FeatureCount() - 1)[columnName] = "";
				}
			}
		}
	}

	public void TrianglePointMs(int t, ref PointM p0, ref PointM p1, ref PointM p2)
	{
		p0 = _points[Triangles[t].p0];
		p1 = _points[Triangles[t].p1];
		p2 = _points[Triangles[t].p2];
	}

	public bool Rasterize(RasterM raster, float[] values)
	{
		_Closure_0024__43_002D0 capturedLocals = default;
		_Closure_0024__43_002D0 capturedLocals2 = new _Closure_0024__43_002D0(capturedLocals);
		capturedLocals2.floatValues = values;
		if (!raster.Overlaps(_extent))
		{
			return false;
		}
		capturedLocals2.cellsSet = false;
		RasterizeTriangle rasterizeTriangle = new RasterizeTriangle(raster);
		checked
		{
			int count = Triangles.Count - 1;
			PointM p = default;
			PointM p2 = default;
			PointM p3 = default;
			for (int i = 0; i <= count; i++)
			{
				TrianglePointMs(i, ref p, ref p2, ref p3);
				rasterizeTriangle.ComputeCellsZ(p, p2, p3, [SpecialName] (int n, float z) =>
				{
					capturedLocals2.floatValues[n] = z;
					capturedLocals2.cellsSet = true;
				});
			}
			return capturedLocals2.cellsSet;
		}
	}

	public bool Rasterize(RasterM raster, bool[] mask)
	{
		_Closure_0024__44_002D0 capturedLocals = default;
		_Closure_0024__44_002D0 capturedLocals2 = new _Closure_0024__44_002D0(capturedLocals);
		capturedLocals2.mask = mask;
		if (!raster.Overlaps(_extent))
		{
			return false;
		}
		capturedLocals2.cellsSet = false;
		RasterizeTriangle rasterizeTriangle = new RasterizeTriangle(raster);
		checked
		{
			int count = Triangles.Count - 1;
			PointM p = default;
			PointM p2 = default;
			PointM p3 = default;
			for (int i = 0; i <= count; i++)
			{
				TrianglePointMs(i, ref p, ref p2, ref p3);
				rasterizeTriangle.ComputeCells(p, p2, p3, [SpecialName] (int n) =>
				{
					capturedLocals2.mask[n] = true;
					capturedLocals2.cellsSet = true;
				});
			}
			return capturedLocals2.cellsSet;
		}
	}

	private int[] MakeTriangleKeepMap(ref int triangleCount)
	{
		checked
		{
			int[] triangleKeepMap = new int[_triangles.Count - 1 + 1];
			triangleCount = 0;
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!_triangles[i].IsDeleted())
				{
					triangleKeepMap[i] = triangleCount;
					triangleCount++;
				}
				else
				{
					triangleKeepMap[i] = -1;
				}
			}
			return triangleKeepMap;
		}
	}

	private bool[] GetUsedPointArray()
	{
		checked
		{
			bool[] usedPointArray = new bool[_points.Count - 1 + 1];
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				if (!developmentTriangle.IsDeleted())
				{
					usedPointArray[developmentTriangle.p0] = true;
					usedPointArray[developmentTriangle.p1] = true;
					usedPointArray[developmentTriangle.p2] = true;
				}
			}
			return usedPointArray;
		}
	}

	private int[] GetRemappedPoints(bool[] pointsUsed, ref int pointCount)
	{
		checked
		{
			int[] remappedPoints = new int[_points.Count - 1 + 1];
			pointCount = 0;
			int count = _points.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (pointsUsed[i])
				{
					remappedPoints[i] = pointCount;
					pointCount++;
				}
				else
				{
					remappedPoints[i] = -1;
				}
			}
			return remappedPoints;
		}
	}

	private PointMs GetFinalPoints(int pointCount, bool[] pointsUsed)
	{
		PointMs pointMs = new PointMs();
		pointMs.Capacity = pointCount;
		checked
		{
			int count = _points.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (pointsUsed[i])
				{
					pointMs.Add(_points[i]);
				}
			}
			return pointMs;
		}
	}

	private TriangleStruct[] GetFinalTriangles(int triangleCount, int[] pointKeepMap, int[] triangleKeepMap)
	{
		checked
		{
			TriangleStruct[] triangleStructs = new TriangleStruct[triangleCount - 1 + 1];
			int count = _triangles.Count - 1;
			TriangleStruct triangleStruct = default;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				if (!developmentTriangle.IsDeleted())
				{
					triangleStruct.p0 = pointKeepMap[developmentTriangle.p0];
					triangleStruct.p1 = pointKeepMap[developmentTriangle.p1];
					triangleStruct.p2 = pointKeepMap[developmentTriangle.p2];
					if (developmentTriangle.at0 == -1)
					{
						triangleStruct.at0 = -1;
					}
					else
					{
						triangleStruct.at0 = triangleKeepMap[developmentTriangle.at0];
					}
					if (developmentTriangle.at1 == -1)
					{
						triangleStruct.at1 = -1;
					}
					else
					{
						triangleStruct.at1 = triangleKeepMap[developmentTriangle.at1];
					}
					if (developmentTriangle.at2 == -1)
					{
						triangleStruct.at2 = -1;
					}
					else
					{
						triangleStruct.at2 = triangleKeepMap[developmentTriangle.at2];
					}
					triangleStruct.EdgeType = developmentTriangle.GetFlags();
					triangleStructs[triangleKeepMap[i]] = triangleStruct;
				}
			}
			return triangleStructs;
		}
	}

	protected double CrossProduct(PointM a, PointM b, PointM c)
	{
		return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
	}

	public PolygonFeatureLayer DebugExportTrisAsPolygonLayer(string name)
	{
		PolygonFeatureLayer polygonFeatureLayer = new PolygonFeatureLayer(name);
		polygonFeatureLayer.IsDebugLayer = true;
		polygonFeatureLayer.AddAttributeColumn("Deleted", typeof(bool));
		polygonFeatureLayer.AddAttributeColumn("p0", typeof(int));
		polygonFeatureLayer.AddAttributeColumn("p1", typeof(int));
		polygonFeatureLayer.AddAttributeColumn("p2", typeof(int));
		polygonFeatureLayer.AddAttributeColumn("at0", typeof(int));
		polygonFeatureLayer.AddAttributeColumn("at1", typeof(int));
		polygonFeatureLayer.AddAttributeColumn("at2", typeof(int));
		polygonFeatureLayer.AddAttributeColumn("Edge 0", typeof(string));
		polygonFeatureLayer.AddAttributeColumn("Edge 1", typeof(string));
		polygonFeatureLayer.AddAttributeColumn("Edge 2", typeof(string));
		checked
		{
			int count = Triangles.Count - 1;
			PointM p = default;
			PointM p2 = default;
			PointM p3 = default;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = Triangles[i];
				TrianglePointMs(i, ref p, ref p2, ref p3);
				Polygon polygon = new Polygon(new PointMs(new PointM[3] { p, p2, p3 }));
				if (polygon.Area < 0.0)
				{
					polygon = polygon.Reverse();
				}
				DataRow dataRow = polygonFeatureLayer.AddFeature(polygon);
				dataRow["Deleted"] = developmentTriangle.IsDeleted();
				dataRow["p0"] = developmentTriangle.p0;
				dataRow["p1"] = developmentTriangle.p1;
				dataRow["p2"] = developmentTriangle.p2;
				dataRow["at0"] = developmentTriangle.at0;
				dataRow["at1"] = developmentTriangle.at1;
				dataRow["at2"] = developmentTriangle.at2;
				string dataRow2 = "";
				if (developmentTriangle.IsEdge0Breakline())
				{
					dataRow2 = "Breakline";
				}
				else
				{
					dataRow2 = ((!developmentTriangle.IsEdge0Perimeter()) ? "No Boundary" : "Perimeter");
				}
				dataRow["Edge 0"] = dataRow2;
				string dataRow3 = "";
				if (developmentTriangle.IsEdge1Breakline())
				{
					dataRow3 = "Breakline";
				}
				else
				{
					dataRow3 = ((!developmentTriangle.IsEdge1Perimeter()) ? "No Boundary" : "Perimeter");
				}
				dataRow["Edge 1"] = dataRow3;
				string dataRow4 = "";
				if (developmentTriangle.IsEdge2Breakline())
				{
					dataRow4 = "Breakline";
				}
				else
				{
					dataRow4 = ((!developmentTriangle.IsEdge2Perimeter()) ? "No Boundary" : "Perimeter");
				}
				dataRow["Edge 2"] = dataRow4;
			}
			return polygonFeatureLayer;
		}
	}

	public PolylineFeatureLayer DebugExportEdges(string name)
	{
		PolylineFeatureLayer polylineFeatureLayer = new PolylineFeatureLayer(name);
		polylineFeatureLayer.IsDebugLayer = true;
		polylineFeatureLayer.AddAttributeColumn("Edge Type", typeof(string));
		checked
		{
			int count = Triangles.Count - 1;
			PointM p = default;
			PointM p2 = default;
			PointM p3 = default;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = Triangles[i];
				TrianglePointMs(i, ref p, ref p2, ref p3);
				Polyline feature = new Polyline(new PointMs(new PointM[2] { p, p2 }));
				DataRow dataRow = polylineFeatureLayer.AddFeature(feature);
				string dataRow4 = "";
				if (developmentTriangle.IsEdge0Breakline())
				{
					dataRow4 = "Breakline";
				}
				else
				{
					dataRow4 = ((!developmentTriangle.IsEdge0Perimeter()) ? "No Boundary" : "Perimeter");
				}
				dataRow["Edge Type"] = dataRow4;
				Polyline feature2 = new Polyline(new PointMs(new PointM[2] { p2, p3 }));
				DataRow dataRow2 = polylineFeatureLayer.AddFeature(feature2);
				string dataRow22 = "";
				if (developmentTriangle.IsEdge1Breakline())
				{
					dataRow22 = "Breakline";
				}
				else
				{
					dataRow22 = ((!developmentTriangle.IsEdge1Perimeter()) ? "No Boundary" : "Perimeter");
				}
				dataRow2["Edge Type"] = dataRow22;
				Polyline feature3 = new Polyline(new PointMs(new PointM[2] { p3, p }));
				DataRow dataRow3 = polylineFeatureLayer.AddFeature(feature3);
				string dataRow32 = "";
				if (developmentTriangle.IsEdge2Breakline())
				{
					dataRow32 = "Breakline";
				}
				else
				{
					dataRow32 = ((!developmentTriangle.IsEdge2Perimeter()) ? "No Boundary" : "Perimeter");
				}
				dataRow3["Edge Type"] = dataRow32;
			}
			return polylineFeatureLayer;
		}
	}

	public void DebugExportShapefile(string filename)
	{
		PolygonFeatureLayer featureLayer = DebugExportTrisAsPolygonLayer("empty");
		string summary = "";
		ShapefileStorage.WriteShapefile(featureLayer, filename, ref summary);
	}
}
