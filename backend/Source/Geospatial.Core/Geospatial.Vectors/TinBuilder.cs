using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Geospatial.Vectors;

public class TinBuilder
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("TinBuilder");

	private readonly List<TriangleDefinition> _triangles = new List<TriangleDefinition>();

	private readonly PointCollection _points = new PointCollection();

	private Extent _extent;

	private int _boundaryPointStart;

	private readonly double _magnetizationTolerance;

	private readonly double _magnetizationToleranceSquared;

	private readonly Point _normalizeOrigin;

	private readonly Vector _toNormal;

	private RasterDefinition _lattice = RasterDefinition.NullRaster;

	private readonly Dictionary<(int, int), int> _edgeConstraints = new Dictionary<(int, int), int>();

	private int _lastT;

	public Extent ValidExtent => _extent;

	public Vector ToNormalVector => _toNormal;

	public TinBuilder(Point normalizeOrigin, double magnetizationTolerance)
	{
		_normalizeOrigin = normalizeOrigin;
		_magnetizationTolerance = magnetizationTolerance;
		_magnetizationToleranceSquared = magnetizationTolerance * magnetizationTolerance;
		_toNormal = new Vector(0.0 - normalizeOrigin.X, 0.0 - normalizeOrigin.Y);
		_extent = Extent.CenteredOn(_normalizeOrigin, 1.0);
		Init();
	}

	public TinBuilder(RasterDefinition lattice, double magnetizationTolerance)
	{
		_magnetizationTolerance = magnetizationTolerance;
		_magnetizationToleranceSquared = magnetizationTolerance * magnetizationTolerance;
		_normalizeOrigin = lattice.Extent.Center;
		_toNormal = new Vector(0.0 - _normalizeOrigin.X, 0.0 - _normalizeOrigin.Y);
		lattice = lattice.Translate(_toNormal);
		_extent = lattice.Extent;
		InitLattice(lattice);
	}

	private static void GetOffsets(int global, out int ul, out int um, out int ur, out int mr, out int lr, out int lm, out int ll, out int ml)
	{
		ul = global;
		um = global + 1;
		ur = global + 2;
		mr = global + 3;
		lr = global + 4;
		lm = global + 5;
		ll = global + 6;
		ml = global + 7;
	}

	private void Init()
	{
		_boundaryPointStart = 0;
		AddBoundingPoints(_extent);
		_points.Add(_extent.Center);
		int p = _points.Count - 1;
		GetOffsets(_boundaryPointStart, out var ul, out var um, out var ur, out var mr, out var lr, out var lm, out var ll, out var ml);
		_triangles.Add(new TriangleDefinition(ul, p, um, 7, 1, -1));
		_triangles.Add(new TriangleDefinition(um, p, ur, 0, 2, -1));
		_triangles.Add(new TriangleDefinition(ur, p, mr, 1, 3, -1));
		_triangles.Add(new TriangleDefinition(mr, p, lr, 2, 4, -1));
		_triangles.Add(new TriangleDefinition(lr, p, lm, 3, 5, -1));
		_triangles.Add(new TriangleDefinition(lm, p, ll, 4, 6, -1));
		_triangles.Add(new TriangleDefinition(ll, p, ml, 5, 7, -1));
		_triangles.Add(new TriangleDefinition(ml, p, ul, 6, 0, -1));
	}

	private int ReasonableStartingTriangle(Point pt)
	{
		if (_lattice.IsNull())
		{
			return 0;
		}
		return _lattice.CellIndex(pt) * 4;
	}

	private void InitLattice(RasterDefinition rdef)
	{
		_lattice = rdef;
		_extent = rdef.Extent;
		_boundaryPointStart = int.MinValue;
		int rowsRdefTerm = (rdef.Rows + 1) * (rdef.Cols + 1);
		int capacity = rowsRdefTerm + rdef.Rows * rdef.Cols;
		_points.Capacity = capacity;
		for (int i = 0; i < rdef.Rows + 1; i++)
		{
			double y = rdef.CellMaxY(i);
			for (int j = 0; j < rdef.Cols + 1; j++)
			{
				double x = rdef.CellMinX(j);
				_points.Add(x, y);
			}
		}
		for (int k = 0; k < rdef.Rows; k++)
		{
			double y2 = rdef.CellCenterY(k);
			for (int l = 0; l < rdef.Cols; l++)
			{
				double x2 = rdef.CellCenterX(l);
				_points.Add(x2, y2);
			}
		}
		for (int m = 0; m < rdef.Rows; m++)
		{
			for (int n = 0; n < rdef.Cols; n++)
			{
				int colsRdefTerm = m * rdef.Cols + n;
				int p = rowsRdefTerm + colsRdefTerm;
				int colsRdefTerm2 = m * (rdef.Cols + 1) + n;
				int colsRdefTerm3 = m * (rdef.Cols + 1) + n + 1;
				int colsRdefTerm4 = (m + 1) * (rdef.Cols + 1) + n;
				int colsRdefTerm5 = (m + 1) * (rdef.Cols + 1) + n + 1;
				int ordinal = 4 * colsRdefTerm;
				int ordinal2 = ordinal + 1;
				int ordinal3 = ordinal + 2;
				int ordinal4 = ordinal + 3;
				int at = ((m > 0) ? (4 * (colsRdefTerm - rdef.Cols) + 2) : (-1));
				int at2 = ((m < rdef.Rows - 1) ? (4 * (colsRdefTerm + rdef.Cols)) : (-1));
				int at3 = ((n > 0) ? (4 * (colsRdefTerm - 1) + 3) : (-1));
				int at4 = ((n < rdef.Cols - 1) ? (4 * (colsRdefTerm + 1) + 1) : (-1));
				TriangleDefinition triangleDefinition = new TriangleDefinition(p, colsRdefTerm3, colsRdefTerm2, ordinal4, at, ordinal2);
				TriangleDefinition triangleDefinition2 = new TriangleDefinition(p, colsRdefTerm2, colsRdefTerm4, ordinal, at3, ordinal3);
				TriangleDefinition triangleDefinition3 = new TriangleDefinition(p, colsRdefTerm4, colsRdefTerm5, ordinal2, at2, ordinal4);
				TriangleDefinition triangleDefinition4 = new TriangleDefinition(p, colsRdefTerm5, colsRdefTerm3, ordinal3, at4, ordinal);
				_triangles.Add(triangleDefinition);
				_triangles.Add(triangleDefinition2);
				_triangles.Add(triangleDefinition3);
				_triangles.Add(triangleDefinition4);
			}
		}
	}

	private void AddBoundingPoints(Extent ext)
	{
		_points.Add(ext.UpperLeft);
		_points.Add(ext.UpperMidside);
		_points.Add(ext.UpperRight);
		_points.Add(ext.RightMidside);
		_points.Add(ext.LowerRight);
		_points.Add(ext.LowerMidside);
		_points.Add(ext.LowerLeft);
		_points.Add(ext.LeftMidside);
	}

	public Tin ToTin(bool deNormalize = true)
	{
		Tin tin = new Tin(_points, _triangles, isCleaned: true);
		if (deNormalize)
		{
			tin.Translate(_toNormal.Reverse());
		}
		return tin;
	}

	public void EnsureInclude(Extent ext)
	{
		EnsureIncludeInternal(ext.Translate(_toNormal));
	}

	private void EnsureIncludeInternal(Point pt)
	{
		if (!_lattice.IsNull())
		{
			if (!_extent.Contains(pt))
			{
				throw new Exception("Cannot resize latticed grids.");
			}
		}
		else
		{
			EnsureIncludeInternal(Extent.CenteredOn(pt, _magnetizationTolerance * 2.0));
		}
	}

	private void EnsureIncludeInternal(Extent ext)
	{
		if (!_lattice.IsNull())
		{
			if (!_extent.Contains(ext))
			{
				throw new Exception("Cannot resize latticed grids.");
			}
			return;
		}
		ext = ext.EnforceMinDimensions(_magnetizationTolerance, _magnetizationTolerance).RatioAroundCenter(1.0 + _magnetizationTolerance);
		while (!_extent.Contains(ext))
		{
			Extent extent = _extent.RatioAroundCenter(2.0);
			int boundaryPointStart = _boundaryPointStart;
			int count = _points.Count;
			int count2 = _triangles.Count;
			GetOffsets(boundaryPointStart, out var ul, out var um, out var ur, out var mr, out var lr, out var lm, out var ll, out var ml);
			GetOffsets(count, out var ul2, out var um2, out var ur2, out var mr2, out var lr2, out var lm2, out var ll2, out var ml2);
			int t = 0;
			FindTriangleContainingPoint(new Point(_extent.Left + _extent.Width * 0.25, _extent.Top - _magnetizationTolerance), ref t);
			int ordinal = t;
			FindTriangleContainingPoint(new Point(_extent.Left + _extent.Width * 0.75, _extent.Top - _magnetizationTolerance), ref t);
			int ordinal2 = t;
			FindTriangleContainingPoint(new Point(_extent.Right - _magnetizationTolerance, _extent.Bottom + _extent.Height * 0.75), ref t);
			int ordinal3 = t;
			FindTriangleContainingPoint(new Point(_extent.Right - _magnetizationTolerance, _extent.Bottom + _extent.Height * 0.25), ref t);
			int ordinal4 = t;
			FindTriangleContainingPoint(new Point(_extent.Left + _extent.Width * 0.25, _extent.Bottom + _magnetizationTolerance), ref t);
			int ordinal5 = t;
			FindTriangleContainingPoint(new Point(_extent.Left + _extent.Width * 0.75, _extent.Bottom + _magnetizationTolerance), ref t);
			int ordinal6 = t;
			FindTriangleContainingPoint(new Point(_extent.Left + _magnetizationTolerance, _extent.Bottom + _extent.Height * 0.25), ref t);
			int ordinal7 = t;
			FindTriangleContainingPoint(new Point(_extent.Left + _magnetizationTolerance, _extent.Bottom + _extent.Height * 0.75), ref t);
			int ordinal8 = t;
			AddBoundingPoints(extent);
			_triangles.Add(new TriangleDefinition(ul2, ul, um2, count2 + 15, count2 + 1, -1));
			_triangles.Add(new TriangleDefinition(um2, ul, um, count2, ordinal, count2 + 2));
			_triangles.Add(new TriangleDefinition(um2, um, ur, count2 + 1, ordinal2, count2 + 3));
			_triangles.Add(new TriangleDefinition(um2, ur, ur2, count2 + 2, count2 + 4, -1));
			_triangles.Add(new TriangleDefinition(ur2, ur, mr2, count2 + 3, count2 + 5, -1));
			_triangles.Add(new TriangleDefinition(mr2, ur, mr, count2 + 4, ordinal3, count2 + 6));
			_triangles.Add(new TriangleDefinition(mr2, mr, lr, count2 + 5, ordinal4, count2 + 7));
			_triangles.Add(new TriangleDefinition(mr2, lr, lr2, count2 + 6, count2 + 8, -1));
			_triangles.Add(new TriangleDefinition(lr2, lr, lm2, count2 + 7, count2 + 9, -1));
			_triangles.Add(new TriangleDefinition(lm2, lr, lm, count2 + 8, ordinal6, count2 + 10));
			_triangles.Add(new TriangleDefinition(lm2, lm, ll, count2 + 9, ordinal5, count2 + 11));
			_triangles.Add(new TriangleDefinition(lm2, ll, ll2, count2 + 10, count2 + 12, -1));
			_triangles.Add(new TriangleDefinition(ll2, ll, ml2, count2 + 11, count2 + 13, -1));
			_triangles.Add(new TriangleDefinition(ml2, ll, ml, count2 + 12, ordinal7, count2 + 14));
			_triangles.Add(new TriangleDefinition(ml2, ml, ul, count2 + 13, ordinal8, count2 + 15));
			_triangles.Add(new TriangleDefinition(ml2, ul, ul2, count2 + 14, count2, -1));
			ReplaceBoundaryAT(_triangles, ordinal, count2 + 1);
			ReplaceBoundaryAT(_triangles, ordinal2, count2 + 2);
			ReplaceBoundaryAT(_triangles, ordinal3, count2 + 5);
			ReplaceBoundaryAT(_triangles, ordinal4, count2 + 6);
			ReplaceBoundaryAT(_triangles, ordinal6, count2 + 9);
			ReplaceBoundaryAT(_triangles, ordinal5, count2 + 10);
			ReplaceBoundaryAT(_triangles, ordinal7, count2 + 13);
			ReplaceBoundaryAT(_triangles, ordinal8, count2 + 14);
			_extent = extent;
			_boundaryPointStart = count;
		}
		static void ReplaceBoundaryAT(List<TriangleDefinition> tdefs, int tIdx, int newBoundaryIdx)
		{
			TriangleDefinition tdef = tdefs[tIdx];
			tdef.ChangeAdjacentTriangle(-1, newBoundaryIdx);
			tdefs[tIdx] = tdef;
		}
	}

	private void OnMagnetized(Point pt, int toPointIndex)
	{
	}

	private void OnMagnetized(Point pt, Point toPoint, int edgePointA, int edgePointB)
	{
	}

	public void BurnSegment(Segment seg)
	{
		seg = seg.Translate(_toNormal);
		EnsureIncludeInternal(seg.Extent);
		InsertPointInternal(seg.A, out var pointIndex, out var adjacentTriIdx);
		while (!BurnSegmentInternal_Continuation(ref adjacentTriIdx, ref pointIndex, seg.B))
		{
		}
	}

	private bool BurnSegmentInternal_Continuation(ref int tIdx, ref int aPtIdx, Point targetPoint)
	{
		Point ptA = _points[aPtIdx];
		Point point = new Segment(ptA, targetPoint).PointAtDistance(_magnetizationTolerance);
		FindTriangleContainingPoint(point, ref tIdx);
		if (GetTriangleInternal(tIdx).Contains(targetPoint))
		{
			InsertPointInternal(targetPoint, out var pointIndex, out var _);
			SetConstrainedEdge(aPtIdx, pointIndex, -1);
			return true;
		}
		TriangleDefinition triangleDefinition = _triangles[tIdx];
		int oppositeE012FromPoint = triangleDefinition.OppositeE012FromPoint(aPtIdx);
		int aPtIdx2 = triangleDefinition.Point(oppositeE012FromPoint);
		int aPtIdx3 = triangleDefinition.Point((oppositeE012FromPoint + 1) % 3);
		Segment segment = new Segment(_points[aPtIdx2], _points[aPtIdx3]);
		if (!segment.Intersects(new Segment(ptA, targetPoint), out var x, out var y, out var t, out var _))
		{
			throw new Exception("Triangulation error - Segments should intersect");
		}
		double lengthSquared = segment.LengthSquared;
		double lengthSquaredTerm = lengthSquared * t;
		if (lengthSquaredTerm < _magnetizationToleranceSquared)
		{
			aPtIdx = aPtIdx2;
		}
		else if (lengthSquared - lengthSquaredTerm < _magnetizationToleranceSquared)
		{
			aPtIdx = aPtIdx3;
		}
		else
		{
			ForceSplitEdge(tIdx, oppositeE012FromPoint, new Point(x, y), out var _, out var _);
		}
		return false;
	}

	private void SetConstrainedEdge(int pA, int pB, int constraintId)
	{
		(int, int) key = ((pA > pB) ? (pB, pA) : (pA, pB));
		_edgeConstraints.Add(key, constraintId);
	}

	private void EnforceEdge(int onTriIdx, int pA, int pB)
	{
		TriangleDefinition triangleDefinition = _triangles[onTriIdx];
		triangleDefinition.Point012ByGlobalIdx(pA);
		triangleDefinition.Point012ByGlobalIdx(pB);
	}

	private void ForceSplitEdge(int triIdx, int e012Best, Point pt, out int pointIndex, out int adjacentTriIdx)
	{
		TriangleDefinition triangleDefinition = _triangles[triIdx];
		int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(e012Best);
		int count = _triangles.Count;
		int countOffset = _triangles.Count + 1;
		TriangleDefinition triangleDefinition2 = _triangles[adjacentTriangleIdx];
		int p2TriangleDefinitionTerm;
		int p0TriangleDefinitionTerm;
		int p1TriangleDefinitionTerm;
		int at;
		int at2TriangleDefinitionTerm;
		switch (e012Best)
		{
		case 0:
			p2TriangleDefinitionTerm = triangleDefinition.p2;
			p0TriangleDefinitionTerm = triangleDefinition.p0;
			p1TriangleDefinitionTerm = triangleDefinition.p1;
			at = triangleDefinition.at1;
			at2TriangleDefinitionTerm = triangleDefinition.at2;
			break;
		case 1:
			p2TriangleDefinitionTerm = triangleDefinition.p0;
			p0TriangleDefinitionTerm = triangleDefinition.p1;
			p1TriangleDefinitionTerm = triangleDefinition.p2;
			at = triangleDefinition.at2;
			at2TriangleDefinitionTerm = triangleDefinition.at0;
			break;
		default:
			p2TriangleDefinitionTerm = triangleDefinition.p1;
			p0TriangleDefinitionTerm = triangleDefinition.p2;
			p1TriangleDefinitionTerm = triangleDefinition.p0;
			at = triangleDefinition.at0;
			at2TriangleDefinitionTerm = triangleDefinition.at1;
			break;
		}
		int thirdPoint = triangleDefinition2.ThirdPoint(p0TriangleDefinitionTerm, p1TriangleDefinitionTerm);
		int count2 = _points.Count;
		int at2 = triangleDefinition2.AdjacentTriangleOppositePoint_GlobalIndex(p0TriangleDefinitionTerm);
		int adjacentTriangleOppositePoint_GlobalIndex = triangleDefinition2.AdjacentTriangleOppositePoint_GlobalIndex(p1TriangleDefinitionTerm);
		_points.Add(pt);
		TriangleDefinition triangles = new TriangleDefinition(count2, p1TriangleDefinitionTerm, p2TriangleDefinitionTerm, adjacentTriangleIdx, at, countOffset);
		TriangleDefinition triangles2 = new TriangleDefinition(count2, thirdPoint, p1TriangleDefinitionTerm, count, at2, triIdx);
		TriangleDefinition triangleDefinition3 = new TriangleDefinition(count2, p0TriangleDefinitionTerm, thirdPoint, countOffset, adjacentTriangleOppositePoint_GlobalIndex, adjacentTriangleIdx);
		TriangleDefinition triangleDefinition4 = new TriangleDefinition(count2, p2TriangleDefinitionTerm, p0TriangleDefinitionTerm, triIdx, at2TriangleDefinitionTerm, count);
		_triangles[triIdx] = triangles;
		_triangles[adjacentTriangleIdx] = triangles2;
		_triangles.Add(triangleDefinition3);
		_triangles.Add(triangleDefinition4);
		ChangeAtPointer(at2TriangleDefinitionTerm, triIdx, countOffset);
		ChangeAtPointer(adjacentTriangleOppositePoint_GlobalIndex, adjacentTriangleIdx, count);
		pointIndex = count2;
		adjacentTriIdx = triIdx;
	}

	private void CastRayFromPoint(int startingPointIdx, Point destinationPoint, int candidateTriIdx)
	{
		Point point = _points[startingPointIdx];
		_ = _triangles[candidateTriIdx];
		point.Translate(point.To(destinationPoint).ScaleTo(_magnetizationTolerance));
	}

	private Triangle GetTriangleInternal(int index)
	{
		TriangleDefinition triangleDefinition = _triangles[index];
		return new Triangle(_points[triangleDefinition.p0], _points[triangleDefinition.p1], _points[triangleDefinition.p2]);
	}

	public void InsertPoint(Point pt)
	{
		pt = pt.Translate(_toNormal);
		EnsureIncludeInternal(pt);
		InsertPointInternal(pt, out var _, out var _);
	}

	private void InsertPointInternal(Point pt, out int pointIndex, out int adjacentTriIdx)
	{
		int t = ReasonableStartingTriangle(pt);
		FindTriangleContainingPoint(pt, ref t);
		TriangleDefinition triangleDefinition = _triangles[t];
		Point point = _points[triangleDefinition.p0];
		if (point.DistanceSquaredTo(pt) <= _magnetizationToleranceSquared)
		{
			pointIndex = triangleDefinition.p0;
			adjacentTriIdx = t;
			return;
		}
		Point point2 = _points[triangleDefinition.p1];
		if (point2.DistanceSquaredTo(pt) <= _magnetizationToleranceSquared)
		{
			pointIndex = triangleDefinition.p1;
			adjacentTriIdx = t;
			return;
		}
		Point point3 = _points[triangleDefinition.p2];
		if (point3.DistanceSquaredTo(pt) <= _magnetizationToleranceSquared)
		{
			pointIndex = triangleDefinition.p2;
			adjacentTriIdx = t;
			return;
		}
		Segment segment = new Segment(point, point2);
		Segment segment2 = new Segment(point2, point3);
		Segment segment3 = new Segment(point3, point);
		bool closeTo = segment.CloseTo(pt, _magnetizationTolerance, out var nearPoint, out var _, out var actualDistance);
		bool closeTo2 = segment2.CloseTo(pt, _magnetizationTolerance, out var nearPoint2, out var _, out actualDistance);
		bool closeTo3 = segment3.CloseTo(pt, _magnetizationTolerance, out var nearPoint3, out var _, out actualDistance);
		if (!closeTo && !closeTo2 && !closeTo3)
		{
			int p = (pointIndex = _points.Count);
			_points.Add(pt);
			int adjacentTriIdx2 = t;
			int count = _triangles.Count;
			int countOffset = _triangles.Count + 1;
			int at = triangleDefinition.at0;
			int at2 = triangleDefinition.at1;
			int at3 = triangleDefinition.at2;
			int p2 = triangleDefinition.p0;
			int p3 = triangleDefinition.p1;
			int p4 = triangleDefinition.p2;
			TriangleDefinition triangles = new TriangleDefinition(p, p2, p3, countOffset, at, count);
			TriangleDefinition triangleDefinition2 = new TriangleDefinition(p, p3, p4, adjacentTriIdx2, at2, countOffset);
			TriangleDefinition triangleDefinition3 = new TriangleDefinition(p, p4, p2, count, at3, adjacentTriIdx2);
			_triangles[t] = triangles;
			_triangles.Add(triangleDefinition2);
			_triangles.Add(triangleDefinition3);
			ChangeAtPointer(at2, t, count);
			ChangeAtPointer(at3, t, countOffset);
			adjacentTriIdx = adjacentTriIdx2;
		}
		else
		{
			int unity;
			Point point4;
			if (closeTo && !closeTo2 && !closeTo3)
			{
				unity = 0;
				point4 = nearPoint;
			}
			else if (closeTo2 && !closeTo && !closeTo3)
			{
				unity = 1;
				point4 = nearPoint2;
			}
			else if (closeTo3 && !closeTo && !closeTo2)
			{
				unity = 2;
				point4 = nearPoint3;
			}
			else
			{
				double d = (closeTo ? pt.DistanceSquaredTo(nearPoint) : double.MaxValue);
				double d2 = (closeTo2 ? pt.DistanceSquaredTo(nearPoint2) : double.MaxValue);
				double d3 = (closeTo3 ? pt.DistanceSquaredTo(nearPoint3) : double.MaxValue);
				unity = MathHelpers.Min3Index(d, d2, d3);
				point4 = unity switch
				{
					0 => nearPoint, 
					1 => nearPoint2, 
					_ => nearPoint3, 
				};
			}
			pt = point4;
			ForceSplitEdge(t, unity, pt, out pointIndex, out adjacentTriIdx);
		}
	}

	private void ChangeAtPointer(int forTriangle, int fromTri, int toTri)
	{
		if (forTriangle >= 0)
		{
			TriangleDefinition triangle = _triangles[forTriangle];
			triangle.ChangeAdjacentTriangle(fromTri, toTri);
			_triangles[forTriangle] = triangle;
		}
	}

	private int FindTriangleContainingPoint(Point point)
	{
		FindTriangleContainingPoint(point, ref _lastT);
		return _lastT;
	}

	private void FindTriangleContainingPoint(Point point, ref int t)
	{
		if (t < 0 || t > _triangles.Count - 1)
		{
			t = 0;
		}
		int ordinal = 0;
		int ordinal2 = -1;
		while (t != -1 && ordinal != _triangles.Count)
		{
			TriangleDefinition triangleDefinition = _triangles[t];
			double d = Vector.CrossProduct(_points[triangleDefinition.p0], _points[triangleDefinition.p1], in point);
			double d2 = Vector.CrossProduct(_points[triangleDefinition.p1], _points[triangleDefinition.p2], in point);
			double d3 = Vector.CrossProduct(_points[triangleDefinition.p2], _points[triangleDefinition.p0], in point);
			MathHelpers.Min3Index(d, d2, d3, out var idx, out var localValue);
			if (localValue >= 0.0)
			{
				return;
			}
			int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(idx);
			if (ordinal2 == adjacentTriangleIdx)
			{
				t = Math.Min(adjacentTriangleIdx, t);
				return;
			}
			ordinal2 = t;
			t = adjacentTriangleIdx;
			ordinal++;
		}
		t = -1;
	}
}
