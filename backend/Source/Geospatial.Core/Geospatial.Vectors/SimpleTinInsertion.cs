using System;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class SimpleTinInsertion : TriangulationStage
{
	public SimpleTinInsertion(Tin tin)
		: base(tin)
	{
	}

	public static void InsertPoints(Tin tin, IList<Point> points)
	{
		SimpleTinInsertion simpleTinInsertion = new SimpleTinInsertion(tin);
		for (int i = 0; i < points.Count; i++)
		{
			simpleTinInsertion.InsertPoint(points[i]);
		}
	}

	public int InsertPoint(Point pt)
	{
		return InsertPoint(new Point4D(pt, _tin.ZFillValue, _tin.MFillValue));
	}

	public int InsertPoint(Point3D pt)
	{
		return InsertPoint(new Point4D(pt, _tin.MFillValue));
	}

	public int InsertPoint(Point4D pt)
	{
		int nearTriIdx;
		return InsertPointInternal(pt, translate: true, out nearTriIdx);
	}

	private int InsertPointInternal(Point4D pt, bool translate, out int nearTriIdx)
	{
		if (translate)
		{
			pt = pt.Translate(_normalizedOffset);
		}
		Point point = pt.AsPoint2D();
		int t = 0;
		_tin.FindTriangleCheckNearbyCornersAndEdges(point, _nearPointTolerance, ref t, out var p, out var ed);
		if (t == -1)
		{
			nearTriIdx = -1;
			return -1;
		}
		if (p >= 0)
		{
			nearTriIdx = t;
			return p;
		}
		if (!ed.IsNull())
		{
			SplitEdge(ed, pt);
		}
		else
		{
			SimpleInsert(t, pt);
		}
		nearTriIdx = t;
		return _tin.PointCount - 1;
	}

	public void InsertSegments(IReadOnlyList<Segment> segments, bool isPerimeter)
	{
		foreach (Segment segment in segments)
		{
			InsertSegment(segment, isPerimeter);
		}
	}

	public void InsertSegment(Segment segment, bool isPerimeter)
	{
		InsertSegment(new Segment4D(new Point4D(segment.A, 0.0, 0.0), new Point4D(segment.B, 0.0, 0.0)), isPerimeter);
	}

	public void InsertSegment(Point a, Point b, bool isPerimeter)
	{
		InsertSegment(new Segment(a, b), isPerimeter);
	}

	public void InsertSegment(Segment4D segment, bool isPerimeter)
	{
		segment = segment.Translate(_normalizedOffset);
		int insertPointInternal = InsertPointInternal(segment.A, translate: false, out var nearTriIdx);
		if (insertPointInternal < 0)
		{
			return;
		}
		Point point = segment.B.AsPoint2D();
		int ordinal = 0;
		int triangleCount = _tin.TriangleCount;
		do
		{
			_tin.FindExit(insertPointInternal, nearTriIdx, point, _nearPointTolerance, out var exitEdge, out var exitTriIdx);
			double t2;
			if (exitTriIdx >= 0)
			{
				int e = _tin.TriangleDefinition[exitTriIdx].OppositeE012FromPoint(insertPointInternal);
				EdgeDefinition edgeDefinition = _tin.GetEdgeDefinition(exitTriIdx, e);
				Segment segment2 = _tin.Edge(exitTriIdx, e);
				double perpendicularSignedDistance = segment2.PerpendicularSignedDistance(point);
				if (perpendicularSignedDistance > _triParams.Tolerance)
				{
					SimpleInsert(exitTriIdx, segment.B);
					return;
				}
				if (!(perpendicularSignedDistance < 0.0 - _triParams.Tolerance))
				{
					Point pt = segment2.NearestPoint(point);
					SplitEdge(edgeDefinition, pt);
					return;
				}
				Segment seg = new Segment(_tin.Points[insertPointInternal], point);
				segment2.IntersectPoint(seg, out var x, out var y, out var _, out t2);
				SplitEdge(edgeDefinition, new Point(x, y));
				insertPointInternal = _tin.PointCount - 1;
				nearTriIdx = exitTriIdx;
			}
			else
			{
				Segment segment3 = _tin.Edge(exitEdge);
				double length = segment3.Length;
				double length2 = new Segment(_tin.Points[insertPointInternal], point).Length;
				t2 = length;
				double? tolerance = _triParams.Tolerance;
				if (length2 < t2 - tolerance)
				{
					Point pt2 = segment3.NearestPoint(point, out t2);
					SplitEdge(exitEdge, pt2);
					int pB = _tin.PointCount - 1;
					if (_tin.TryGetEdgeDefinition(insertPointInternal, pB, new int[4]
					{
						exitEdge.ATriangleIndex,
						exitEdge.BTriangleIndex,
						_tin.TriangleCount - 1,
						_tin.TriangleCount - 2
					}).IsNull())
					{
						throw new Exception("Bad Triangulation");
					}
					return;
				}
				t2 = length2;
				tolerance = _triParams.Tolerance;
				if (!(length < t2 - tolerance))
				{
					return;
				}
				insertPointInternal = exitEdge.OtherPoint(insertPointInternal);
				nearTriIdx = exitEdge.EitherDefinedTriangleIndex();
			}
			ordinal++;
		}
		while (ordinal <= triangleCount);
		throw new Exception("Exceeded max iterations in segment insertion - triangulation error?");
	}

	private void SplitEdge(EdgeDefinition ed, Point pt)
	{
		SplitEdge(ed, new Point4D(pt, _tin.ZFillValue, _tin.MFillValue));
	}

	private void SplitEdge(EdgeDefinition ed, Point4D pt)
	{
		_tin.Add(pt);
		int pointCountOffset = _tin.PointCount - 1;
		int aTriangleIndex = ed.ATriangleIndex;
		int bTriangleIndex = ed.BTriangleIndex;
		int aPointIndex = ed.APointIndex;
		int bPointIndex = ed.BPointIndex;
		if (bTriangleIndex == -1)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[aTriangleIndex];
			int thirdPoint = triangleDefinition.ThirdPoint(aPointIndex, bPointIndex);
			int edge = triangleDefinition.GetEdge012(bPointIndex, thirdPoint);
			int edge2 = triangleDefinition.GetEdge012(aPointIndex, thirdPoint);
			triangleDefinition.GetEdge012(aPointIndex, bPointIndex);
			int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(edge);
			int adjacentTriangleIdx2 = triangleDefinition.AdjacentTriangleIdx(edge2);
			int aTriangleIndex2 = aTriangleIndex;
			int triangleCount = _tin.TriangleCount;
			TriangleDefinition triangleDefinition4 = new TriangleDefinition(aPointIndex, pointCountOffset, thirdPoint, -1, triangleCount, adjacentTriangleIdx2);
			TriangleDefinition triangleDefinition5 = new TriangleDefinition(bPointIndex, thirdPoint, pointCountOffset, adjacentTriangleIdx, aTriangleIndex2, -1);
			if (triangleDefinition.IsDeleted)
			{
				triangleDefinition4.IsDeleted = true;
				triangleDefinition5.IsDeleted = true;
			}
			_tin.TriangleDefinition[aTriangleIndex] = triangleDefinition4;
			_tin.TriangleDefinition.Add(triangleDefinition5);
			UpdateTriangleEdge(adjacentTriangleIdx, bPointIndex, thirdPoint, triangleCount);
			UpdateTriangleEdge(adjacentTriangleIdx2, aPointIndex, thirdPoint, aTriangleIndex2);
		}
		else
		{
			if (aTriangleIndex == -1)
			{
				throw new NotImplementedException();
			}
			TriangleDefinition triangleDefinition2 = _tin.TriangleDefinition[aTriangleIndex];
			TriangleDefinition triangleDefinition3 = _tin.TriangleDefinition[bTriangleIndex];
			int thirdPoint2 = triangleDefinition2.ThirdPoint(aPointIndex, bPointIndex);
			int thirdPoint3 = triangleDefinition3.ThirdPoint(aPointIndex, bPointIndex);
			int edge3 = triangleDefinition2.GetEdge012(bPointIndex, thirdPoint2);
			int edge4 = triangleDefinition2.GetEdge012(aPointIndex, thirdPoint2);
			triangleDefinition2.GetEdge012(aPointIndex, bPointIndex);
			int edge5 = triangleDefinition3.GetEdge012(bPointIndex, thirdPoint3);
			int edge6 = triangleDefinition3.GetEdge012(aPointIndex, thirdPoint3);
			triangleDefinition3.GetEdge012(aPointIndex, bPointIndex);
			int adjacentTriangleIdx3 = triangleDefinition2.AdjacentTriangleIdx(edge3);
			int adjacentTriangleIdx4 = triangleDefinition2.AdjacentTriangleIdx(edge4);
			int adjacentTriangleIdx5 = triangleDefinition3.AdjacentTriangleIdx(edge5);
			int adjacentTriangleIdx6 = triangleDefinition3.AdjacentTriangleIdx(edge6);
			int aTriangleIndex3 = aTriangleIndex;
			int triangleCount2 = _tin.TriangleCount;
			int bTriangleIndex2 = bTriangleIndex;
			int triangleCountOffset = _tin.TriangleCount + 1;
			TriangleDefinition triangleDefinition6 = new TriangleDefinition(aPointIndex, pointCountOffset, thirdPoint2, triangleCountOffset, triangleCount2, adjacentTriangleIdx4);
			TriangleDefinition triangleDefinition7 = new TriangleDefinition(bPointIndex, thirdPoint2, pointCountOffset, adjacentTriangleIdx3, aTriangleIndex3, bTriangleIndex2);
			TriangleDefinition triangleDefinition8 = new TriangleDefinition(bPointIndex, pointCountOffset, thirdPoint3, triangleCount2, triangleCountOffset, adjacentTriangleIdx5);
			TriangleDefinition triangleDefinition9 = new TriangleDefinition(aPointIndex, thirdPoint3, pointCountOffset, adjacentTriangleIdx6, bTriangleIndex2, aTriangleIndex3);
			if (triangleDefinition2.IsDeleted)
			{
				triangleDefinition6.IsDeleted = true;
				triangleDefinition7.IsDeleted = true;
			}
			if (triangleDefinition3.IsDeleted)
			{
				triangleDefinition8.IsDeleted = true;
				triangleDefinition9.IsDeleted = true;
			}
			_tin.TriangleDefinition[aTriangleIndex] = triangleDefinition6;
			_tin.TriangleDefinition.Add(triangleDefinition7);
			_tin.TriangleDefinition[bTriangleIndex] = triangleDefinition8;
			_tin.TriangleDefinition.Add(triangleDefinition9);
			UpdateTriangleEdge(adjacentTriangleIdx3, bPointIndex, thirdPoint2, triangleCount2);
			UpdateTriangleEdge(adjacentTriangleIdx4, aPointIndex, thirdPoint2, aTriangleIndex3);
			UpdateTriangleEdge(adjacentTriangleIdx5, bPointIndex, thirdPoint3, bTriangleIndex2);
			UpdateTriangleEdge(adjacentTriangleIdx6, aPointIndex, thirdPoint3, triangleCountOffset);
		}
		_diagnosticAction?.Invoke(_tin);
	}

	private void SimpleInsert(int triIdx, Point4D pt)
	{
		_tin.Add(pt);
		int pointCountOffset = _tin.PointCount - 1;
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[triIdx];
		int triangleCount = _tin.TriangleCount;
		int triangleCountOffset = triangleCount + 1;
		TriangleDefinition triangleDefinition2 = new TriangleDefinition(triangleDefinition.p0, pointCountOffset, triangleDefinition.p2, triangleCount, triangleCountOffset, triangleDefinition.at2);
		TriangleDefinition triangleDefinition3 = new TriangleDefinition(triangleDefinition.p0, triangleDefinition.p1, pointCountOffset, triangleDefinition.at0, triangleCountOffset, triIdx);
		TriangleDefinition triangleDefinition4 = new TriangleDefinition(triangleDefinition.p2, pointCountOffset, triangleDefinition.p1, triIdx, triangleCount, triangleDefinition.at1);
		triangleDefinition2.IsDeleted = triangleDefinition.IsDeleted;
		triangleDefinition3.IsDeleted = triangleDefinition.IsDeleted;
		triangleDefinition4.IsDeleted = triangleDefinition.IsDeleted;
		_tin.TriangleDefinition[triIdx] = triangleDefinition2;
		_tin.TriangleDefinition.Add(triangleDefinition3);
		_tin.TriangleDefinition.Add(triangleDefinition4);
		UpdateTriangleEdge(triangleDefinition.at0, triangleDefinition.p0, triangleDefinition.p1, triangleCount);
		UpdateTriangleEdge(triangleDefinition.at1, triangleDefinition.p1, triangleDefinition.p2, triangleCountOffset);
		UpdateTriangleEdge(triangleDefinition.at2, triangleDefinition.p2, triangleDefinition.p0, triIdx);
		_diagnosticAction?.Invoke(_tin);
	}

	private void UpdateTriangleEdge(int tIdx, int p1, int p2, int newTriIdx)
	{
		if (tIdx != -1)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[tIdx];
			triangleDefinition.SetAdjacentTriangle(p1, p2, newTriIdx);
			_tin.TriangleDefinition[tIdx] = triangleDefinition;
		}
	}
}
