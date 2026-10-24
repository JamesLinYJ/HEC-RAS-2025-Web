using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Terrain;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using Utility.Progress;

namespace RasMapperLib;

public class DelaunayTINFactory : TINFactory
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__50_002D0
	{
		public BitArray triTester;

		public Stack<int> trianglesToVisitStack;

		public DelaunayTINFactory delaunayTINFactory;

		public _Closure_0024__50_002D0(_Closure_0024__50_002D0 other)
		{
			if (other != null)
			{
				triTester = other.triTester;
				trianglesToVisitStack = other.trianglesToVisitStack;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int triIdx, int atIdx, bool crossesPerim)
		{
			if (atIdx == -1 || triTester[atIdx])
			{
				return;
			}
			trianglesToVisitStack.Push(atIdx);
			triTester.Set(atIdx, value: true);
			DevelopmentTriangle developmentTriangle = delaunayTINFactory._triangles[triIdx];
			DevelopmentTriangle developmentTriangle2 = delaunayTINFactory._triangles[atIdx];
			if (crossesPerim)
			{
				if (!developmentTriangle.IsDeleted())
				{
					developmentTriangle2.MarkDeleted();
				}
			}
			else if (developmentTriangle.IsDeleted())
			{
				developmentTriangle2.MarkDeleted();
			}
		}
	}

	public static int SUPER_POINT_COUNT = 8;

	private Polygon _perimeter;

	private List<Polyline> _breaklines;

	private IPoints _massPoints;

	private StitchCache _stitches;

	private double _maxEdgeLen;

	private Action<int> _markDupes;

	private PointM _normalizedOffset;

	public int MassPointInsertionsStartHere;

	private int _currentIndex;

	public DelaunayTINFactory(double NearPointTolerance)
		: base(NearPointTolerance)
	{
		_perimeter = null;
		_breaklines = null;
		_massPoints = null;
		_stitches = null;
		_maxEdgeLen = double.NaN;
		_currentIndex = 0;
	}

	private void ResetGlobalVars()
	{
		_stitches = null;
		_massPoints = null;
		_breaklines = null;
		_maxEdgeLen = double.NaN;
		_points = new PointMs();
		_triangles = new List<DevelopmentTriangle>();
		_deletedTrianglesStack = new Stack<int>();
	}

	public DevelopmentTin ComputeTIN(Polygon perimeter, List<Polyline> breaklines, IPoints massPoints, StitchCache stitches, bool removeDeletedTriangles = true, double edgeLen = double.NaN, ProgressReporter reporter = null, Action<int> MarkDuplicates = null, Extent MBR = null, double largestFlatTriangleEdge = double.NaN, bool InjectRectangleCenterPoints = false)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		_perimeter = perimeter;
		_breaklines = breaklines;
		_massPoints = massPoints;
		_stitches = stitches;
		_maxEdgeLen = edgeLen;
		_markDupes = MarkDuplicates;
		if (_stitches != null && _stitches.Count == 0)
		{
			_stitches = null;
		}
		if (_perimeter == null && _breaklines == null && _massPoints == null && _stitches == null)
		{
			throw new ArgumentException("DelaunayTINFactory: At least 1 of the following inputs/constraints should be non-null: Perimeter, Breaklines, MassPoints, Stitches.");
		}
		checked
		{
			double scaledCount = ((_stitches != null) ? (_stitches.Count * 2) : 0);
			double pointMCount = ((_massPoints != null) ? _massPoints.PointMCount() : 0);
			double pointMCountTerm = scaledCount / (scaledCount + pointMCount);
			if (double.IsNaN(pointMCountTerm))
			{
				pointMCountTerm = 0.0;
			}
			double computed = 1.0 - pointMCountTerm;
			Extent extent = ComputeMaximalExtent();
			extent = extent.EnforceMinDimensions(_tolerance * 10.0, _tolerance * 10.0);
			extent = extent.EnforceMinDXDYRatio(0.125);
			if (MBR != null)
			{
				extent = extent.UnionXY(MBR);
			}
			_normalizedOffset = ComputeOffset(extent);
			AddOctagonPointsAndTriangles(extent);
			InsertPerimeterPoints();
			InsertBreaklinePoints();
			MassPointInsertionsStartHere = _points.Count;
			ProgressReporter reporter2 = reporter.WithProgressModifier(0, (float)computed);
			InsertMassPoints(reporter2);
			reporter2 = reporter.WithProgressModifier((int)System.Math.Round(100.0 * computed), (float)pointMCountTerm);
			InsertStitchSegments(reporter2);
			if (reporter.CancelRequested)
			{
				ResetGlobalVars();
				return null;
			}
			if (!double.IsNaN(largestFlatTriangleEdge))
			{
				int t = -1;
				double minValueTerm;
				do
				{
					minValueTerm = double.MinValue;
					int count2 = _triangles.Count - 1;
					for (int i = 0; i <= count2; i++)
					{
						if (IsSuperTriangle(i))
						{
							continue;
						}
						DevelopmentTriangle developmentTriangle = _triangles[i];
						PointM pt = _points[developmentTriangle.p0];
						PointM pt2 = _points[developmentTriangle.p1];
						PointM pt3 = _points[developmentTriangle.p2];
						if (!double.IsNaN(pt.Z) && !double.IsNaN(pt2.Z) && !double.IsNaN(pt3.Z) && pt.Z == pt2.Z && pt2.Z == pt3.Z)
						{
							double triangleMaxEdgeLength = TriangleMaxEdgeLength(pt, pt2, pt3);
							if (triangleMaxEdgeLength > largestFlatTriangleEdge && triangleMaxEdgeLength > minValueTerm)
							{
								minValueTerm = triangleMaxEdgeLength;
								t = i;
							}
						}
						if (!double.IsNaN(pt.M) && !double.IsNaN(pt2.M) && !double.IsNaN(pt3.M) && pt.M == pt2.M && pt2.M == pt3.M)
						{
							double triangleMaxEdgeLength2 = TriangleMaxEdgeLength(pt, pt2, pt3);
							if (triangleMaxEdgeLength2 > largestFlatTriangleEdge && triangleMaxEdgeLength2 > minValueTerm)
							{
								minValueTerm = triangleMaxEdgeLength2;
								t = i;
							}
						}
					}
					if (minValueTerm != double.MinValue)
					{
						PointM point = TriangleCenter(t);
						if (!DelaunayInsertPoint(point, translatePoint: false))
						{
							minValueTerm = double.MinValue;
						}
					}
				}
				while (minValueTerm != double.MinValue);
			}
			EnforceInternalMaxEdgeLengthConstraint();
			EnforceBreakLineEdges();
			try
			{
				EnforcePerimeterEdges();
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
			if (InjectRectangleCenterPoints)
			{
				int count = _triangles.Count;
				HashSet<int> intSet = new HashSet<int>();
				PointMs pointMs = new PointMs();
				int count3 = count - 1;
				for (int j = 0; j <= count3; j++)
				{
					if (intSet.Contains(j))
					{
						continue;
					}
					DevelopmentTriangle developmentTriangle2 = _triangles[j];
					TriangleArea(j);
					PointM pointM = _points[_triangles[j].p0];
					PointM pointM2 = _points[_triangles[j].p1];
					PointM pointM3 = _points[_triangles[j].p2];
					DevelopmentTriangle developmentTriangle3 = ((developmentTriangle2.at0 > 0 && !_triangles[developmentTriangle2.at0].IsDeleted()) ? _triangles[developmentTriangle2.at0] : null);
					DevelopmentTriangle developmentTriangle4 = ((developmentTriangle2.at1 > 0 && !_triangles[developmentTriangle2.at1].IsDeleted()) ? _triangles[developmentTriangle2.at1] : null);
					DevelopmentTriangle developmentTriangle5 = ((developmentTriangle2.at2 > 0 && !_triangles[developmentTriangle2.at2].IsDeleted()) ? _triangles[developmentTriangle2.at2] : null);
					if (developmentTriangle3 != null)
					{
						PointM checkIfCurTriangleAndNeighborFormQuad = CheckIfCurTriangleAndNeighborFormQuad(developmentTriangle2, developmentTriangle3, j, developmentTriangle2.at0, pointM3, pointM, pointM2);
						if (checkIfCurTriangleAndNeighborFormQuad.IsDefined())
						{
							intSet.Add(developmentTriangle2.at0);
							pointMs.Add(checkIfCurTriangleAndNeighborFormQuad);
							continue;
						}
					}
					if (developmentTriangle4 != null)
					{
						PointM checkIfCurTriangleAndNeighborFormQuad2 = CheckIfCurTriangleAndNeighborFormQuad(developmentTriangle2, developmentTriangle4, j, developmentTriangle2.at1, pointM, pointM2, pointM3);
						if (checkIfCurTriangleAndNeighborFormQuad2.IsDefined())
						{
							intSet.Add(developmentTriangle2.at1);
							pointMs.Add(checkIfCurTriangleAndNeighborFormQuad2);
							continue;
						}
					}
					if (developmentTriangle5 != null)
					{
						PointM checkIfCurTriangleAndNeighborFormQuad3 = CheckIfCurTriangleAndNeighborFormQuad(developmentTriangle2, developmentTriangle5, j, developmentTriangle2.at2, pointM2, pointM3, pointM);
						if (checkIfCurTriangleAndNeighborFormQuad3.IsDefined())
						{
							intSet.Add(developmentTriangle2.at2);
							pointMs.Add(checkIfCurTriangleAndNeighborFormQuad3);
						}
					}
				}
				if (_massPoints == null)
				{
					_massPoints = new PointMs();
				}
				DelaunayInsertPoints(pointMs, translatePoint: false, null);
			}
			MarkBadTrianglesAsDeleted();
			PointM delta = _normalizedOffset.NegateXY();
			int count4 = _points.Count - 1;
			for (int k = 0; k <= count4; k++)
			{
				_points[k] = _points[k].Translate(delta);
			}
			DevelopmentTin developmentTin = new DevelopmentTin(_triangles, _points, extent);
			ResetGlobalVars();
			if (removeDeletedTriangles)
			{
				developmentTin.RemoveDeletedTriangles();
				MassPointInsertionsStartHere = -1;
			}
			return developmentTin;
		}
	}

	private PointM CheckIfCurTriangleAndNeighborFormQuad(DevelopmentTriangle curTriangle, DevelopmentTriangle neighbor, int curTriangleIndex, int neighborTriangleIndex, PointM unsharedPt, PointM sharedPointA, PointM sharedPointB)
	{
		PointM pointM = _points[neighbor.p0];
		PointM pointM2 = _points[neighbor.p1];
		PointM pointM3 = _points[neighbor.p2];
		if (neighbor.at0 == curTriangleIndex)
		{
			PointM pointM4 = _points[neighbor.p2];
			PointM pointM5 = unsharedPt.MidPoint(pointM4);
			PointM pointM6 = sharedPointA.MidPoint(sharedPointB);
			double distanceTo = unsharedPt.DistanceTo(pointM4);
			double distanceTo2 = sharedPointA.DistanceTo(sharedPointB);
			double distanceTo2OverDistanceTo = distanceTo2 / distanceTo;
			double distanceTo3 = unsharedPt.DistanceTo(sharedPointA);
			double distanceTo4 = unsharedPt.DistanceTo(sharedPointB);
			double distanceTo4OverDistanceTo3 = ((!(distanceTo3 > distanceTo4)) ? (distanceTo4 / distanceTo3) : (distanceTo3 / distanceTo4));
			if (pointM5.CloseTo(pointM6, distanceTo2 * 0.01) && distanceTo2OverDistanceTo > 0.99 && distanceTo2OverDistanceTo < 1.01 && distanceTo4OverDistanceTo3 < 4.0)
			{
				double z = (unsharedPt.Z + pointM.Z + pointM2.Z + pointM3.Z) / 4.0;
				PointM result = pointM5.MidPoint(pointM6);
				result.Z = z;
				return result;
			}
		}
		else if (neighbor.at1 == curTriangleIndex)
		{
			PointM pointM4 = _points[neighbor.p0];
			PointM pointM7 = unsharedPt.MidPoint(pointM4);
			PointM pointM8 = sharedPointA.MidPoint(sharedPointB);
			double distanceTo5 = unsharedPt.DistanceTo(pointM4);
			double distanceTo6 = sharedPointA.DistanceTo(sharedPointB);
			double distanceTo6OverDistanceTo5 = distanceTo6 / distanceTo5;
			double distanceTo7 = unsharedPt.DistanceTo(sharedPointA);
			double distanceTo8 = unsharedPt.DistanceTo(sharedPointB);
			double distanceTo8OverDistanceTo7 = ((!(distanceTo7 > distanceTo8)) ? (distanceTo8 / distanceTo7) : (distanceTo7 / distanceTo8));
			if (pointM7.CloseTo(pointM8, distanceTo6 * 0.01) && distanceTo6OverDistanceTo5 > 0.99 && distanceTo6OverDistanceTo5 < 1.01 && distanceTo8OverDistanceTo7 < 4.0)
			{
				double z2 = (unsharedPt.Z + pointM.Z + pointM2.Z + pointM3.Z) / 4.0;
				PointM result2 = pointM7.MidPoint(pointM8);
				result2.Z = z2;
				return result2;
			}
		}
		else
		{
			PointM pointM4 = _points[neighbor.p1];
			PointM pointM9 = unsharedPt.MidPoint(pointM4);
			PointM pointM10 = sharedPointA.MidPoint(sharedPointB);
			double distanceTo9 = unsharedPt.DistanceTo(pointM4);
			double distanceTo10 = sharedPointA.DistanceTo(sharedPointB);
			double distanceTo10OverDistanceTo9 = distanceTo10 / distanceTo9;
			double distanceTo11 = unsharedPt.DistanceTo(sharedPointA);
			double distanceTo12 = unsharedPt.DistanceTo(sharedPointB);
			double distanceTo12OverDistanceTo11 = ((!(distanceTo11 > distanceTo12)) ? (distanceTo12 / distanceTo11) : (distanceTo11 / distanceTo12));
			if (pointM9.CloseTo(pointM10, distanceTo10 * 0.01) && distanceTo10OverDistanceTo9 > 0.99 && distanceTo10OverDistanceTo9 < 1.01 && distanceTo12OverDistanceTo11 < 4.0)
			{
				double z3 = (unsharedPt.Z + pointM.Z + pointM2.Z + pointM3.Z) / 4.0;
				PointM result3 = pointM9.MidPoint(pointM10);
				result3.Z = z3;
				return result3;
			}
		}
		return PointM.Undefined();
	}

	public double TriangleArea(int t)
	{
		int p = _triangles[t].p0;
		int p2 = _triangles[t].p1;
		int p3 = _triangles[t].p2;
		return Tin.TriangleArea(_points[p].X, _points[p].Y, _points[p2].X, _points[p2].Y, _points[p3].X, _points[p3].Y);
	}

	private bool IsSuperPoint(int idx)
	{
		return idx < SUPER_POINT_COUNT;
	}

	private bool IsSuperTriangle(int t)
	{
		DevelopmentTriangle developmentTriangle = _triangles[t];
		if (developmentTriangle.p0 < SUPER_POINT_COUNT || developmentTriangle.p1 < SUPER_POINT_COUNT || developmentTriangle.p2 < SUPER_POINT_COUNT)
		{
			return true;
		}
		return false;
	}

	private double TriangleMaxEdgeLength(PointM pt0, PointM pt1, PointM pt2)
	{
		double d = pt0.DistanceTo(pt1);
		double d2 = pt1.DistanceTo(pt2);
		double d3 = pt2.DistanceTo(pt0);
		return RasMapperLib.Utilities.Math.Max3(d, d2, d3);
	}

	private PointM TriangleCenter(int t)
	{
		DevelopmentTriangle developmentTriangle = _triangles[t];
		PointM pointM = _points[developmentTriangle.p0];
		PointM pointM2 = _points[developmentTriangle.p1];
		PointM pointM3 = _points[developmentTriangle.p2];
		double ax = (pointM.X + pointM2.X + pointM3.X) / 3.0;
		double ay = (pointM.Y + pointM2.Y + pointM3.Y) / 3.0;
		return new PointM(ax, ay);
	}

	private double TrianglesMaxM(int t)
	{
		int p = _triangles[t].p0;
		int p2 = _triangles[t].p1;
		int p3 = _triangles[t].p2;
		return RasMapperLib.Utilities.Math.Max3(_points[p].M, _points[p2].M, _points[p3].M);
	}

	private void DelaunayInsertPoints(IPoints points, bool translatePoint, ProgressReporter reporter)
	{
		if (points == null)
		{
			return;
		}
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				_currentIndex = i;
				PointM point = points.PointM(i);
				DelaunayInsertPoint(point, translatePoint);
				if (unchecked(i % 1000) == 0)
				{
					reporter.ReportProgress((int)System.Math.Round(100.0 * ((double)i / (double)points.PointMCount())));
				}
			}
		}
	}

	private bool DelaunayInsertPoint(PointM point, bool translatePoint)
	{
		if (translatePoint)
		{
			point = point.Translate(_normalizedOffset);
		}
		int p = default;
		int t = default;
		FindTriangleAndPointIndexes(point, ref t, ref p);
		if (p >= 0)
		{
			UpdatePointZM(p, point);
			if (_markDupes != null)
			{
				_markDupes(_currentIndex);
			}
			return false;
		}
		p = _points.Count;
		_points.Add(point);
		Stack<int> intStack = new Stack<int>();
		intStack.Push(t);
		while (intStack.Count != 0)
		{
			t = intStack.Pop();
			_deletedTrianglesStack.Push(t);
			DevelopmentTriangle developmentTriangle = _triangles[t];
			developmentTriangle.MarkDeleted();
			if (IsTriangleInsideInsertionHole(developmentTriangle.at0, point))
			{
				intStack.Push(developmentTriangle.at0);
			}
			if (IsTriangleInsideInsertionHole(developmentTriangle.at1, point))
			{
				intStack.Push(developmentTriangle.at1);
			}
			if (IsTriangleInsideInsertionHole(developmentTriangle.at2, point))
			{
				intStack.Push(developmentTriangle.at2);
			}
		}
		MakeInsertionHoleFromDeletedTrianglesAndFill(p);
		return true;
	}

	private bool IsColinear(int idx)
	{
		DevelopmentTriangle developmentTriangle = _triangles[idx];
		if (_points[developmentTriangle.p0].Y == _points[developmentTriangle.p1].Y && _points[developmentTriangle.p0].Y == _points[developmentTriangle.p2].Y)
		{
			return true;
		}
		return false;
	}

	private List<InsertionHoleEdge> MakeInsertionHoleFromDeletedTrianglesAndFill(int p)
	{
		List<InsertionHoleEdge> insertionHoleEdges = new List<InsertionHoleEdge>();
		foreach (int deletedTrianglesStack in _deletedTrianglesStack)
		{
			DevelopmentTriangle developmentTriangle = _triangles[deletedTrianglesStack];
			if (developmentTriangle.at0 == -1 || !_triangles[developmentTriangle.at0].IsDeleted())
			{
				insertionHoleEdges.Add(new InsertionHoleEdge(developmentTriangle.p0, developmentTriangle.p1, developmentTriangle.at0, -1));
			}
			if (developmentTriangle.at1 == -1 || !_triangles[developmentTriangle.at1].IsDeleted())
			{
				insertionHoleEdges.Add(new InsertionHoleEdge(developmentTriangle.p1, developmentTriangle.p2, developmentTriangle.at1, -1));
			}
			if (developmentTriangle.at2 == -1 || !_triangles[developmentTriangle.at2].IsDeleted())
			{
				insertionHoleEdges.Add(new InsertionHoleEdge(developmentTriangle.p2, developmentTriangle.p0, developmentTriangle.at2, -1));
			}
		}
		Dictionary<int, InsertionHolePointTriangles> insertionHolePointTrianglesByInt = new Dictionary<int, InsertionHolePointTriangles>();
		checked
		{
			int count = insertionHoleEdges.Count - 1;
			bool isEdge0BreaklineDevelopmentTriangle2Term = default;
			bool isEdge0PerimeterDevelopmentTriangle2Term = default;
			for (int i = 0; i <= count; i++)
			{
				InsertionHoleEdge insertionHoleEdge = insertionHoleEdges[i];
				int insideTriangleInsertionHoleEdgeTerm = (insertionHoleEdge.insideTriangle = AddTriangle(insertionHoleEdge.p0, insertionHoleEdge.p1, p, insertionHoleEdge.outsideTriangle, -1, -1));
				insertionHoleEdges[i] = insertionHoleEdge;
				InsertionHolePointTriangles insertionHolePointTriangles = default;
				InsertionHolePointTriangles insertionHolePointTriangles2 = default;
				bool tryGetValue = insertionHolePointTrianglesByInt.TryGetValue(insertionHoleEdge.p0, out insertionHolePointTriangles);
				bool tryGetValue2 = insertionHolePointTrianglesByInt.TryGetValue(insertionHoleEdge.p1, out insertionHolePointTriangles2);
				insertionHolePointTriangles.triangleRight = insideTriangleInsertionHoleEdgeTerm;
				insertionHolePointTriangles2.triangleLeft = insideTriangleInsertionHoleEdgeTerm;
				if (tryGetValue)
				{
					insertionHolePointTrianglesByInt[insertionHoleEdge.p0] = insertionHolePointTriangles;
				}
				else
				{
					insertionHolePointTrianglesByInt.Add(insertionHoleEdge.p0, insertionHolePointTriangles);
				}
				if (tryGetValue2)
				{
					insertionHolePointTrianglesByInt[insertionHoleEdge.p1] = insertionHolePointTriangles2;
				}
				else
				{
					insertionHolePointTrianglesByInt.Add(insertionHoleEdge.p1, insertionHolePointTriangles2);
				}
				if (insertionHoleEdge.outsideTriangle >= 0)
				{
					DevelopmentTriangle developmentTriangle2 = _triangles[insertionHoleEdge.outsideTriangle];
					if (developmentTriangle2.p0 == insertionHoleEdge.p1)
					{
						developmentTriangle2.at0 = insideTriangleInsertionHoleEdgeTerm;
						isEdge0BreaklineDevelopmentTriangle2Term = developmentTriangle2.IsEdge0Breakline();
						isEdge0PerimeterDevelopmentTriangle2Term = developmentTriangle2.IsEdge0Perimeter();
					}
					else if (developmentTriangle2.p1 == insertionHoleEdge.p1)
					{
						developmentTriangle2.at1 = insideTriangleInsertionHoleEdgeTerm;
						isEdge0BreaklineDevelopmentTriangle2Term = developmentTriangle2.IsEdge1Breakline();
						isEdge0PerimeterDevelopmentTriangle2Term = developmentTriangle2.IsEdge1Perimeter();
					}
					else if (developmentTriangle2.p2 == insertionHoleEdge.p1)
					{
						developmentTriangle2.at2 = insideTriangleInsertionHoleEdgeTerm;
						isEdge0BreaklineDevelopmentTriangle2Term = developmentTriangle2.IsEdge2Breakline();
						isEdge0PerimeterDevelopmentTriangle2Term = developmentTriangle2.IsEdge2Perimeter();
					}
					if (isEdge0BreaklineDevelopmentTriangle2Term | isEdge0PerimeterDevelopmentTriangle2Term)
					{
						_triangles[insideTriangleInsertionHoleEdgeTerm].SetEdge(0, isEdge0PerimeterDevelopmentTriangle2Term);
					}
				}
			}
			foreach (InsertionHoleEdge insertionHoleEdge2 in insertionHoleEdges)
			{
				DevelopmentTriangle developmentTriangle3 = _triangles[insertionHoleEdge2.insideTriangle];
				developmentTriangle3.at1 = insertionHolePointTrianglesByInt[insertionHoleEdge2.p1].triangleRight;
				developmentTriangle3.at2 = insertionHolePointTrianglesByInt[insertionHoleEdge2.p0].triangleLeft;
			}
			return insertionHoleEdges;
		}
	}

	private bool IsTriangleInsideInsertionHole(int at, PointM point)
	{
		if (at == -1)
		{
			return false;
		}
		DevelopmentTriangle developmentTriangle = _triangles[at];
		if (!developmentTriangle.IsDeleted() && developmentTriangle.CircumCircleCompute(_points).ContainsPoint(point))
		{
			PointM p = _points[developmentTriangle.p0];
			PointM pointM = _points[developmentTriangle.p1];
			PointM p2 = _points[developmentTriangle.p2];
			if ((!IsXColinear(p, pointM, point) || ((!(point.Y > p.Y) || !(point.Y > pointM.Y)) && (!(point.Y < p.Y) || !(point.Y < pointM.Y)))) && (!IsXColinear(p, p2, point) || ((!(point.Y > p.Y) || !(point.Y > p2.Y)) && (!(point.Y < p.Y) || !(point.Y < p2.Y)))) && (!IsXColinear(pointM, p2, point) || ((!(point.Y > pointM.Y) || !(point.Y > p2.Y)) && (!(point.Y < pointM.Y) || !(point.Y < p2.Y)))) && (!IsYColinear(p, pointM, point) || ((!(point.X > p.X) || !(point.X > pointM.X)) && (!(point.X < p.X) || !(point.X < pointM.X)))) && (!IsYColinear(p, p2, point) || ((!(point.X > p.X) || !(point.X > p2.X)) && (!(point.X < p.X) || !(point.X < p2.X)))) && (!IsYColinear(pointM, p2, point) || ((!(point.X > pointM.X) || !(point.X > p2.X)) && (!(point.X < pointM.X) || !(point.X < p2.X)))))
			{
				return true;
			}
		}
		return false;
	}

	private bool IsXColinear(PointM p1, PointM p2, PointM p3)
	{
		if (p1.X == p2.X && p2.X == p3.X)
		{
			return true;
		}
		return false;
	}

	private bool IsYColinear(PointM p1, PointM p2, PointM p3)
	{
		if (p1.Y == p2.Y && p2.Y == p3.Y)
		{
			return true;
		}
		return false;
	}

	public int TrianglePointBeforePoint(int t, int p)
	{
		if (_triangles[t].p0 == p)
		{
			return _triangles[t].p2;
		}
		if (_triangles[t].p1 == p)
		{
			return _triangles[t].p0;
		}
		return _triangles[t].p1;
	}

	public int TrianglePointAfterPoint(int t, int p)
	{
		if (_triangles[t].p0 == p)
		{
			return _triangles[t].p1;
		}
		if (_triangles[t].p1 == p)
		{
			return _triangles[t].p2;
		}
		return _triangles[t].p0;
	}

	private void EnforceEdgeSegments(ISegments segments, bool perimeter)
	{
		checked
		{
			if (segments != null)
			{
				int count = segments.SegmentCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					EnforceEdgeSegment(segments.Segment(i), perimeter);
				}
			}
		}
	}

	private void EnforceEdgeSegment(SegmentM segment, bool perimeter)
	{
		segment = segment.Translate(_normalizedOffset);
		int t = default;
		int p = default;
		FindTriangleAndNearestPointIndex(segment.B, ref t, ref p);
		segment.B = _points[p];
		checked
		{
			int t2 = default;
			int p2 = default;
			double x = default;
			double y = default;
			double t3 = default;
			double t4 = default;
			while (true)
			{
				FindTriangleAndNearestPointIndex(segment.A, ref t2, ref p2);
				segment.A = _points[p2];
				DevelopmentTriangle developmentTriangle;
				int unity;
				SegmentM seg;
				while (true)
				{
					developmentTriangle = _triangles[t2];
					int t22;
					int t23;
					if (p2 == developmentTriangle.p0)
					{
						unity = 1;
						t22 = developmentTriangle.at0;
						t23 = developmentTriangle.at2;
					}
					else if (p2 == developmentTriangle.p1)
					{
						unity = 2;
						t22 = developmentTriangle.at1;
						t23 = developmentTriangle.at0;
					}
					else
					{
						unity = 0;
						t22 = developmentTriangle.at2;
						t23 = developmentTriangle.at1;
					}
					seg = Edge(t2, unity);
					if (CrossProduct(_points[p2], seg.A, segment.B) < 0.0)
					{
						t2 = t22;
						continue;
					}
					if (!(CrossProduct(_points[p2], seg.B, segment.B) > 0.0))
					{
						break;
					}
					t2 = t23;
				}
				if (developmentTriangle.p0 == p || developmentTriangle.p1 == p || developmentTriangle.p2 == p)
				{
					break;
				}
				segment.Intersects(seg, ref x, ref y, ref t3, ref t4, 0.0);
				PointM pointM = segment.PointAtFraction(t3);
				int p22 = EdgeFirstPoint(t2, unity);
				int p23 = EdgeLastPoint(t2, unity);
				if (pointM.CloseTo(seg.A, _tolerance))
				{
					SetEdgeType(t2, p2, p22, perimeter);
					p2 = p22;
					UpdatePointZM(p2, pointM);
					segment.A = _points[p2];
					continue;
				}
				if (pointM.CloseTo(seg.B, _tolerance))
				{
					SetEdgeType(t2, p2, p23, perimeter);
					p2 = p23;
					UpdatePointZM(p2, pointM);
					segment.A = _points[p2];
					continue;
				}
				int count = _points.Count;
				_points.Add(pointM);
				int adjacentTriangle = AdjacentTriangle(t2, unity);
				bool isEdge0BreaklineDevelopmentTriangleTerm;
				bool isEdge0PerimeterDevelopmentTriangleTerm;
				switch (unity)
				{
				case 0:
					isEdge0BreaklineDevelopmentTriangleTerm = developmentTriangle.IsEdge0Breakline();
					isEdge0PerimeterDevelopmentTriangleTerm = developmentTriangle.IsEdge0Perimeter();
					break;
				case 1:
					isEdge0BreaklineDevelopmentTriangleTerm = developmentTriangle.IsEdge1Breakline();
					isEdge0PerimeterDevelopmentTriangleTerm = developmentTriangle.IsEdge1Perimeter();
					break;
				default:
					isEdge0BreaklineDevelopmentTriangleTerm = developmentTriangle.IsEdge2Breakline();
					isEdge0PerimeterDevelopmentTriangleTerm = developmentTriangle.IsEdge2Perimeter();
					break;
				}
				_deletedTrianglesStack.Push(t2);
				_deletedTrianglesStack.Push(adjacentTriangle);
				_triangles[t2].MarkDeleted();
				_triangles[adjacentTriangle].MarkDeleted();
				List<InsertionHoleEdge> insertionHoleFromDeletedTrianglesAndFill = MakeInsertionHoleFromDeletedTrianglesAndFill(count);
				if (isEdge0BreaklineDevelopmentTriangleTerm | isEdge0PerimeterDevelopmentTriangleTerm)
				{
					int count2 = insertionHoleFromDeletedTrianglesAndFill.Count - 1;
					for (int i = 0; i <= count2; i++)
					{
						SetEdgeType(insertionHoleFromDeletedTrianglesAndFill[i].insideTriangle, count, p22, isEdge0PerimeterDevelopmentTriangleTerm);
						SetEdgeType(insertionHoleFromDeletedTrianglesAndFill[i].insideTriangle, count, p23, isEdge0PerimeterDevelopmentTriangleTerm);
					}
				}
				int t5 = _triangles.Count - 1;
				SetEdgeType(t5, p2, count, perimeter);
				segment.A = pointM;
			}
			SetEdgeType(t2, p2, p, perimeter);
		}
	}

	private void DeleteTrianglesLeftOfStitchEdge(SegmentM segment)
	{
		segment = segment.Translate(_normalizedOffset);
		int t = default;
		int p = default;
		FindTriangleAndPointIndexes(segment.A, ref t, ref p);
		while (true)
		{
			DevelopmentTriangle developmentTriangle = _triangles[t];
			int at0DevelopmentTriangleTerm;
			int at2DevelopmentTriangleTerm;
			int e;
			if (p == developmentTriangle.p0)
			{
				e = 1;
				at0DevelopmentTriangleTerm = developmentTriangle.at0;
				at2DevelopmentTriangleTerm = developmentTriangle.at2;
			}
			else if (p == developmentTriangle.p1)
			{
				e = 2;
				at0DevelopmentTriangleTerm = developmentTriangle.at1;
				at2DevelopmentTriangleTerm = developmentTriangle.at0;
			}
			else
			{
				e = 0;
				at0DevelopmentTriangleTerm = developmentTriangle.at2;
				at2DevelopmentTriangleTerm = developmentTriangle.at1;
			}
			SegmentM segmentM = Edge(t, e);
			if (CrossProduct(_points[p], segmentM.A, segment.B) < 0.0)
			{
				t = at0DevelopmentTriangleTerm;
				continue;
			}
			if (CrossProduct(_points[p], segmentM.B, segment.B) > 0.0)
			{
				t = at2DevelopmentTriangleTerm;
				continue;
			}
			PointM a = segmentM.A;
			SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
			double distanceToPoint = segment.DistanceToPoint(a, ref relativePosition);
			PointM b = segmentM.B;
			relativePosition = SegmentM.RelativePosition.Undefined;
			double distanceToPoint2 = segment.DistanceToPoint(b, ref relativePosition);
			if (distanceToPoint < distanceToPoint2)
			{
				p = EdgeFirstPoint(t, e);
				e = e012Before(e);
				int adjacentTriangle = AdjacentTriangle(t, e);
				_triangles[adjacentTriangle].MarkDeleted();
				RotateAroundPoint_DeleteTriangles(adjacentTriangle);
			}
			else
			{
				p = EdgeLastPoint(t, e);
				_triangles[t].MarkDeleted();
				RotateAroundPoint_DeleteTriangles(t);
			}
			if (!_points[p].CloseTo(segment.B, _tolerance))
			{
				continue;
			}
			break;
		}
	}

	private void DeleteTrianglesLeftOfStitchEdge_DeleteAdjacentTriangles(int t)
	{
		if (_triangles[t].at0 != -1 && !_triangles[_triangles[t].at0].IsAnyEdgePerimOrBreakline())
		{
			_triangles[_triangles[t].at0].MarkDeleted();
		}
		if (_triangles[t].at1 != -1 && !_triangles[_triangles[t].at1].IsAnyEdgePerimOrBreakline())
		{
			_triangles[_triangles[t].at1].MarkDeleted();
		}
		if (_triangles[t].at2 != -1 && !_triangles[_triangles[t].at2].IsAnyEdgePerimOrBreakline())
		{
			_triangles[_triangles[t].at2].MarkDeleted();
		}
	}

	private void RotateAroundPoint_DeleteTriangles(int tStart)
	{
		DevelopmentTriangle developmentTriangle = _triangles[tStart];
		RotateAroundPoint_DeleteTriangles(tStart, developmentTriangle.p0);
		RotateAroundPoint_DeleteTriangles(tStart, developmentTriangle.p1);
		RotateAroundPoint_DeleteTriangles(tStart, developmentTriangle.p2);
	}

	private void RotateAroundPoint_DeleteTriangles(int tStart, int pt)
	{
		DevelopmentTriangle developmentTriangle = _triangles[tStart];
		developmentTriangle.MarkDeleted();
		bool isEdgeBLorPerim = default;
		while (true)
		{
			int cWTriangle = developmentTriangle.GetCWTriangle(pt, ref isEdgeBLorPerim);
			if (isEdgeBLorPerim || cWTriangle == -1 || cWTriangle == tStart || IsSuperTriangle(cWTriangle))
			{
				break;
			}
			developmentTriangle = _triangles[cWTriangle];
			developmentTriangle.MarkDeleted();
		}
		bool isEdgeBLorPerim2 = default;
		while (true)
		{
			int cWTriangle = developmentTriangle.GetCCWTriangle(pt, ref isEdgeBLorPerim2);
			if (!isEdgeBLorPerim2 && cWTriangle != -1 && cWTriangle != tStart && !IsSuperTriangle(cWTriangle))
			{
				developmentTriangle = _triangles[cWTriangle];
				developmentTriangle.MarkDeleted();
				continue;
			}
			break;
		}
	}

	private void AddOctagonPointsAndTriangles(Extent extent)
	{
		Extent extent2 = extent.Translate(_normalizedOffset);
		double scaledMaxDeltaXY = extent2.MaxDeltaXY * 0.05;
		_points.Add(extent2.UpperMidside.Translate(0.0, scaledMaxDeltaXY * 2.0));
		_points.Add(extent2.UpperRight.Translate(scaledMaxDeltaXY, scaledMaxDeltaXY));
		_points.Add(extent2.RightMidside.Translate(scaledMaxDeltaXY * 2.0, 0.0));
		_points.Add(extent2.LowerRight.Translate(scaledMaxDeltaXY, 0.0 - scaledMaxDeltaXY));
		_points.Add(extent2.LowerMidside.Translate(0.0, (0.0 - scaledMaxDeltaXY) * 2.0));
		_points.Add(extent2.LowerLeft.Translate(0.0 - scaledMaxDeltaXY, 0.0 - scaledMaxDeltaXY));
		_points.Add(extent2.LeftMidside.Translate((0.0 - scaledMaxDeltaXY) * 2.0, 0.0));
		_points.Add(extent2.UpperLeft.Translate(0.0 - scaledMaxDeltaXY, scaledMaxDeltaXY));
		if (extent.DeltaX > extent.DeltaY)
		{
			AddTriangle(0, 7, 6, -1, -1, 4);
			AddTriangle(6, 5, 4, -1, -1, 4);
			AddTriangle(4, 3, 2, -1, -1, 5);
			AddTriangle(2, 1, 0, -1, -1, 5);
			AddTriangle(0, 6, 4, 0, 1, 5);
			AddTriangle(0, 4, 2, 4, 2, 3);
		}
		else
		{
			AddTriangle(0, 7, 6, -1, -1, 4);
			AddTriangle(6, 5, 4, -1, -1, 5);
			AddTriangle(4, 3, 2, -1, -1, 5);
			AddTriangle(2, 1, 0, -1, -1, 4);
			AddTriangle(0, 6, 2, 0, 5, 3);
			AddTriangle(6, 4, 2, 1, 2, 4);
		}
	}

	private void AddSuperTriangle(Extent extent)
	{
		Extent extent2 = extent.Translate(_normalizedOffset);
		_ = extent2.MaxDeltaXY;
		double maxXAboveMinX = extent2.MaxX - extent2.MinX;
		double maxYAboveMinY = extent2.MaxY - extent2.MinY;
		_points.Add(extent2.LowerRight.Translate(1.0 * maxXAboveMinX, -1.0 * maxYAboveMinY));
		_points.Add(extent2.UpperLeft.Translate(0.5 * maxXAboveMinX, 1.5 * maxYAboveMinY));
		_points.Add(extent2.LowerLeft.Translate(-1.0 * maxXAboveMinX, -1.0 * maxYAboveMinY));
		AddTriangle(0, 1, 2, -1, -1, -1);
	}

	private Extent ComputeMaximalExtent()
	{
		Extent extent = null;
		if (_perimeter != null && _perimeter.Count != 0)
		{
			extent = Vector.MaximumExtent(extent, _perimeter);
		}
		if (_breaklines != null)
		{
			foreach (Polyline breakline in _breaklines)
			{
				if (breakline != null && breakline.Count != 0)
				{
					extent = Vector.MaximumExtent(extent, breakline);
				}
			}
		}
		if (_massPoints != null && _massPoints.PointMCount() != 0)
		{
			extent = Vector.MaximumExtent(extent, _massPoints.ComputeExtent());
		}
		if (_stitches != null)
		{
			foreach (SegmentM stitche in _stitches.Stitches())
			{
				extent = Vector.MaximumExtent(extent, stitche);
			}
		}
		return extent;
	}

	private PointM ComputeOffset(Extent extent)
	{
		PointM result = extent.LowerLeft.NegateXY();
		result.Z = 0.0;
		result.M = 0.0;
		return result;
	}

	private void InsertMassPoints(ProgressReporter reporter)
	{
		if (_massPoints != null)
		{
			DelaunayInsertPoints(_massPoints, translatePoint: true, reporter);
		}
	}

	private void InsertPerimeterPoints()
	{
		if (_perimeter == null)
		{
			return;
		}
		DelaunayInsertPoints(_perimeter, translatePoint: true, null);
		if (double.IsNaN(_maxEdgeLen))
		{
			return;
		}
		checked
		{
			int count = _perimeter.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointMs pointMs = _perimeter.Segment(i).AddDensifyPoints(_maxEdgeLen);
				if (pointMs.Count != 0)
				{
					DelaunayInsertPoints(pointMs, translatePoint: true, null);
				}
			}
		}
	}

	private void InsertBreaklinePoints()
	{
		if (_breaklines == null)
		{
			return;
		}
		checked
		{
			foreach (Polyline breakline in _breaklines)
			{
				DelaunayInsertPoints(breakline, translatePoint: true, null);
				if (double.IsNaN(_maxEdgeLen))
				{
					continue;
				}
				int count = breakline.SegmentCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					PointMs pointMs = breakline.Segment(i).AddDensifyPoints(_maxEdgeLen);
					if (pointMs.Count != 0)
					{
						DelaunayInsertPoints(pointMs, translatePoint: true, null);
					}
				}
			}
		}
	}

	private void InsertStitchSegments(ProgressReporter reporter)
	{
		if (_stitches == null)
		{
			return;
		}
		int ordinal = 0;
		checked
		{
			foreach (SegmentM stitche in _stitches.Stitches())
			{
				DelaunayInsertPoint(stitche.A, translatePoint: true);
				DelaunayInsertPoint(stitche.B, translatePoint: true);
				if (unchecked(ordinal % 100) == 0)
				{
					reporter.ReportProgress((int)System.Math.Round(50.0 * ((double)ordinal / (double)_stitches.Count)));
					if (reporter.CancelRequested)
					{
						return;
					}
				}
				ordinal++;
			}
			ordinal = 0;
			foreach (SegmentM stitche2 in _stitches.Stitches())
			{
				EnforceEdgeSegment(stitche2, perimeter: false);
				if (unchecked(ordinal % 100) == 0)
				{
					reporter.ReportProgress(50 + (int)System.Math.Round(50.0 * ((double)(ordinal + 1) / (double)_stitches.Count)));
					if (reporter.CancelRequested)
					{
						break;
					}
				}
				ordinal++;
			}
		}
	}

	private void EnforceInternalMaxEdgeLengthConstraint()
	{
		if (double.IsNaN(_maxEdgeLen))
		{
			return;
		}
		checked
		{
			int ordinal;
			do
			{
				ordinal = 0;
				int count = _triangles.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					int ordinal2 = 0;
					do
					{
						if (!IsSuperPoint(EdgeFirstPoint(i, ordinal2)) && !IsSuperPoint(EdgeLastPoint(i, ordinal2)))
						{
							SegmentM segmentM = Edge(i, ordinal2);
							if (segmentM.Length > _maxEdgeLen)
							{
								PointM point = segmentM.MidPointXY();
								if (!DelaunayInsertPoint(point, translatePoint: false))
								{
									break;
								}
								ordinal++;
							}
						}
						ordinal2++;
					}
					while (ordinal2 <= 2);
				}
			}
			while (ordinal != 0);
		}
	}

	private void EnforceBreakLineEdges()
	{
		if (_breaklines == null)
		{
			return;
		}
		foreach (Polyline breakline in _breaklines)
		{
			EnforceEdgeSegments(breakline, perimeter: false);
		}
	}

	private void EnforcePerimeterEdges()
	{
		if (_perimeter != null)
		{
			EnforceEdgeSegments(_perimeter, perimeter: true);
		}
	}

	private void MarkBadTrianglesAsDeleted()
	{
		if (_perimeter == null || _perimeter.PartsCount() == 1)
		{
			MarkBadTrianglesAsDeleted_SinglePart();
		}
		else
		{
			MarkBadTrianglesAsDeleted_MultiPart();
		}
	}

	private void Case123()
	{
	}

	private void MarkBadTrianglesAsDeleted_SinglePart()
	{
		Stack<int> intStack = new Stack<int>();
		checked
		{
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				if (IsSuperPoint(developmentTriangle.p0) || IsSuperPoint(developmentTriangle.p1) || IsSuperPoint(developmentTriangle.p2))
				{
					developmentTriangle.MarkDeleted();
					intStack.Push(i);
				}
			}
			if (_perimeter != null && _perimeter.Count > 3)
			{
				while (intStack.Count != 0)
				{
					int index = intStack.Pop();
					DevelopmentTriangle developmentTriangle2 = _triangles[index];
					developmentTriangle2.MarkDeleted();
					if (developmentTriangle2.at0 != -1 && !_triangles[developmentTriangle2.at0].IsDeleted() && !developmentTriangle2.IsEdge0Perimeter())
					{
						intStack.Push(developmentTriangle2.at0);
					}
					if (developmentTriangle2.at1 != -1 && !_triangles[developmentTriangle2.at1].IsDeleted() && !developmentTriangle2.IsEdge1Perimeter())
					{
						intStack.Push(developmentTriangle2.at1);
					}
					if (developmentTriangle2.at2 != -1 && !_triangles[developmentTriangle2.at2].IsDeleted() && !developmentTriangle2.IsEdge2Perimeter())
					{
						intStack.Push(developmentTriangle2.at2);
					}
				}
			}
			if (_stitches == null)
			{
				return;
			}
			double stitcheTerm = 0.0;
			foreach (SegmentM stitche in _stitches.Stitches())
			{
				if (stitcheTerm < stitche.A.M)
				{
					stitcheTerm = stitche.A.M;
				}
			}
			double computed = 2.0001;
			stitcheTerm *= computed;
			int count2 = _triangles.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				DevelopmentTriangle developmentTriangle3 = _triangles[j];
				if (!developmentTriangle3.IsDeleted())
				{
					PointM pointM = _points[developmentTriangle3.p0];
					PointM pointM2 = _points[developmentTriangle3.p1];
					PointM pointM3 = _points[developmentTriangle3.p2];
					if (System.Math.Abs(pointM.X - pointM2.X) > stitcheTerm || System.Math.Abs(pointM.Y - pointM2.Y) > stitcheTerm)
					{
						developmentTriangle3.MarkDeleted();
					}
					else if (System.Math.Abs(pointM2.X - pointM3.X) > stitcheTerm || System.Math.Abs(pointM2.Y - pointM3.Y) > stitcheTerm)
					{
						developmentTriangle3.MarkDeleted();
					}
					else if (System.Math.Abs(pointM3.X - pointM.X) > stitcheTerm || System.Math.Abs(pointM3.Y - pointM.Y) > stitcheTerm)
					{
						developmentTriangle3.MarkDeleted();
					}
				}
			}
			foreach (SegmentM stitche2 in _stitches.Stitches())
			{
				DeleteTrianglesLeftOfStitchEdge(stitche2);
			}
		}
	}

	private void MarkBadTrianglesAsDeleted_MultiPart()
	{
		_Closure_0024__50_002D0 capturedLocals = default;
		_Closure_0024__50_002D0 capturedLocals2 = new _Closure_0024__50_002D0(capturedLocals);
		capturedLocals2.delaunayTINFactory = this;
		capturedLocals2.trianglesToVisitStack = new Stack<int>();
		capturedLocals2.triTester = new BitArray(_triangles.Count);
		checked
		{
			int count = _triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DevelopmentTriangle developmentTriangle = _triangles[i];
				if (IsSuperPoint(developmentTriangle.p0) || IsSuperPoint(developmentTriangle.p1) || IsSuperPoint(developmentTriangle.p2))
				{
					capturedLocals2.triTester.Set(i, value: true);
					capturedLocals2.trianglesToVisitStack.Push(i);
					developmentTriangle.MarkDeleted();
				}
			}
			if (_perimeter != null && _perimeter.Count > 3)
			{
				Action<int, int, bool> action = [SpecialName] (int triIdx, int atIdx, bool crossesPerim) =>
				{
					if (atIdx != -1 && !capturedLocals2.triTester[atIdx])
					{
						capturedLocals2.trianglesToVisitStack.Push(atIdx);
						capturedLocals2.triTester.Set(atIdx, value: true);
						DevelopmentTriangle developmentTriangle4 = capturedLocals2.delaunayTINFactory._triangles[triIdx];
						DevelopmentTriangle developmentTriangle5 = capturedLocals2.delaunayTINFactory._triangles[atIdx];
						if (crossesPerim)
						{
							if (!developmentTriangle4.IsDeleted())
							{
								developmentTriangle5.MarkDeleted();
							}
						}
						else if (developmentTriangle4.IsDeleted())
						{
							developmentTriangle5.MarkDeleted();
						}
					}
				};
				while (capturedLocals2.trianglesToVisitStack.Count != 0)
				{
					int pop = capturedLocals2.trianglesToVisitStack.Pop();
					DevelopmentTriangle developmentTriangle2 = _triangles[pop];
					action(pop, developmentTriangle2.at0, developmentTriangle2.IsEdge0Perimeter());
					action(pop, developmentTriangle2.at1, developmentTriangle2.IsEdge1Perimeter());
					action(pop, developmentTriangle2.at2, developmentTriangle2.IsEdge2Perimeter());
				}
			}
			if (_stitches == null)
			{
				return;
			}
			double stitcheTerm = 0.0;
			foreach (SegmentM stitche in _stitches.Stitches())
			{
				if (stitcheTerm < stitche.A.M)
				{
					stitcheTerm = stitche.A.M;
				}
			}
			double computed = 2.0001;
			stitcheTerm *= computed;
			int count2 = _triangles.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				DevelopmentTriangle developmentTriangle3 = _triangles[intervalIndex];
				if (!developmentTriangle3.IsDeleted())
				{
					PointM pointM = _points[developmentTriangle3.p0];
					PointM pointM2 = _points[developmentTriangle3.p1];
					PointM pointM3 = _points[developmentTriangle3.p2];
					if (System.Math.Abs(pointM.X - pointM2.X) > stitcheTerm || System.Math.Abs(pointM.Y - pointM2.Y) > stitcheTerm)
					{
						developmentTriangle3.MarkDeleted();
					}
					else if (System.Math.Abs(pointM2.X - pointM3.X) > stitcheTerm || System.Math.Abs(pointM2.Y - pointM3.Y) > stitcheTerm)
					{
						developmentTriangle3.MarkDeleted();
					}
					else if (System.Math.Abs(pointM3.X - pointM.X) > stitcheTerm || System.Math.Abs(pointM3.Y - pointM.Y) > stitcheTerm)
					{
						developmentTriangle3.MarkDeleted();
					}
				}
			}
			foreach (SegmentM stitche2 in _stitches.Stitches())
			{
				DeleteTrianglesLeftOfStitchEdge(stitche2);
			}
		}
	}

	public void WriteInputDataToImmediateWindow()
	{
		if (_perimeter != null)
		{
			PointMs pointMs = new PointMs();
			_perimeter.CopyTo(pointMs);
			pointMs.WriteToImmediateWindowAsPointMsCode("perimeter");
		}
		if (_breaklines != null)
		{
			foreach (Polyline breakline in _breaklines)
			{
				PointMs pointMs2 = new PointMs();
				breakline.CopyTo(pointMs2);
				pointMs2.WriteToImmediateWindowAsPointMsCode("breakline");
			}
		}
		if (_stitches == null)
		{
			return;
		}
		foreach (SegmentM stitche in _stitches.Stitches())
		{
			stitche.ToImmediateWindow();
		}
	}
}
