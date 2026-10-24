using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors;

public class DelaunayTinFactory : TinFactory
{
	protected struct InsertionHolePointTriangles
	{
		public int LeftTriangle;

		public int RightTriangle;
	}

	protected struct InsertionHoleEdge(int p0, int p1, int atOutside, int atInside)
	{
		public int p0 = p0;

		public int p1 = p1;

		public int OutsideTriangle = atOutside;

		public int InsideTriangle = atInside;
	}

	public const int SUPER_POINT_COUNT = 8;

	protected bool _retriangulateConstrainedFace;

	protected readonly List<ComplexPolygon> _polygons = new List<ComplexPolygon>();

	protected readonly List<Polyline> _delaunayBreaklines = new List<Polyline>();

	protected readonly PointCollection _delaunayPoints = new PointCollection();

	private Action<int> _markDupes;

	private readonly List<InsertionHoleEdge> _insertionEdges = new List<InsertionHoleEdge>();

	private readonly Dictionary<int, InsertionHolePointTriangles> _insertionHolePoints = new Dictionary<int, InsertionHolePointTriangles>();

	private readonly Stack<int> _trisToDeleteStack = new Stack<int>();

	protected Dictionary<(int, int), int> _perimConstraints = new Dictionary<(int, int), int>();

	protected Dictionary<(int, int), int> _blineConstraints = new Dictionary<(int, int), int>();

	protected Dictionary<int, (int, int)> _perimConstraintsReverseLookup;

	protected Dictionary<int, (int, int)> _blineConstraintsReverseLookup;

	public double MaxEdgeLength { get; set; } = double.NaN;

	public Action<int> MarkDuplicatePoints
	{
		get
		{
			return _markDupes;
		}
		set
		{
			_markDupes = value;
		}
	}

	public Action<ConstrainedEdge, ConstrainedEdge> OnEdgeOverConstrained { get; set; }

	public bool KeepDeletedTriangles { get; set; } = true;

	protected Dictionary<int, (int, int)> PerimConstraintsReverseLookup
	{
		get
		{
			if (_perimConstraintsReverseLookup == null)
			{
				_perimConstraintsReverseLookup = ComputeConstraintReverseLookup(_perimConstraints);
			}
			return _perimConstraintsReverseLookup;
		}
	}

	protected Dictionary<int, (int, int)> BreaklineConstraintsReverseLookup
	{
		get
		{
			if (_blineConstraintsReverseLookup == null)
			{
				_blineConstraintsReverseLookup = ComputeConstraintReverseLookup(_blineConstraints);
			}
			return _blineConstraintsReverseLookup;
		}
	}

	public DelaunayTinFactory(TriangulationParameters tparams = null)
		: base(tparams)
	{
	}

	public void AddPerimeter(Polygon ring)
	{
		_polygons.Add(ComplexPolygon.FromOuterRing(ring));
	}

	public void AddPerimeter(ComplexPolygon complex)
	{
		_polygons.Add(complex);
	}

	public void AddBreakline(Polyline breakline)
	{
		_delaunayBreaklines.Add(breakline);
	}

	public void AddBreaklines(IList<Polyline> breaklines)
	{
		_delaunayBreaklines.AddRange(breaklines);
	}

	public void AddMassPoints(PointCollection pc)
	{
		_delaunayPoints.AddRange(pc);
	}

	public void AddMassPoints(IList<Point> pc)
	{
		_delaunayPoints.AddRange(pc);
	}

	public void AddMassPoint(Point pt)
	{
		_delaunayPoints.Add(pt);
	}

	public bool HasInputs()
	{
		if (_delaunayBreaklines.Count <= 0 && _delaunayPoints.Count <= 0)
		{
			return _polygons.Count > 0;
		}
		return true;
	}

	public Tin ComputeTIN(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (!ValidateMessages(out var errMsg))
		{
			reporter.ReportError(errMsg);
			throw new Exception(errMsg);
		}
		bool hasZ = _polygons.SelectMany((ComplexPolygon cp) => cp.Rings).Any((Polygon ring) => ring.HasZ) || _delaunayBreaklines.Any((Polyline bl) => bl.HasZ) || _delaunayPoints.HasZ;
		bool hasM = _polygons.SelectMany((ComplexPolygon cp) => cp.Rings).Any((Polygon ring) => ring.HasM) || _delaunayBreaklines.Any((Polyline bl) => bl.HasM) || _delaunayPoints.HasM;
		_tin = new Tin();
		if (hasZ)
		{
			_tin.TrackZ();
		}
		if (hasM)
		{
			_tin.TrackM();
		}
		double delaunayPointCount = ((_delaunayPoints != null) ? _delaunayPoints.Count : 0);
		double roughInsertionCount = GetRoughInsertionCount();
		double roughInsertionCountDelaunayPointCountTerm = roughInsertionCount / (roughInsertionCount + delaunayPointCount);
		if (double.IsNaN(roughInsertionCountDelaunayPointCountTerm))
		{
			roughInsertionCountDelaunayPointCountTerm = 0.0;
		}
		double computed = 1.0 - roughInsertionCountDelaunayPointCountTerm;
		Extent extent = ComputeMaximalExtent();
		if (TriangulationParameters.Tolerance.HasValue)
		{
			_nearPointTolerance = TriangulationParameters.Tolerance.Value;
		}
		else
		{
			_nearPointTolerance = TinFactory.ComputeNearPointTolerance(extent);
		}
		double scaledNearPointTolerance = _nearPointTolerance * 10.0;
		extent = extent.EnforceMinDimensions(scaledNearPointTolerance, scaledNearPointTolerance).EnforceMinDXDYRatio(0.125);
		if (TriangulationParameters.EnforcedMBR.HasValue)
		{
			extent = extent.Union(TriangulationParameters.EnforcedMBR.Value);
		}
		if (!TriangulationParameters.NormalizedOffset.HasValue)
		{
			_normalizedOffset = ComputeOffset(extent);
		}
		else
		{
			_normalizedOffset = TriangulationParameters.NormalizedOffset.Value;
		}
		AddOctagonPointsAndTriangles(extent);
		InsertPerimeterPoints();
		InsertBreaklinePoints();
		ProgressReporter progressReporter = null;
		if (_delaunayPoints.Count > 0)
		{
			progressReporter = reporter.WithProgressModifier(0, (float)computed);
			DelaunayInsertPoints(_delaunayPoints, translatePoint: true, progressReporter);
		}
		progressReporter = reporter.WithProgressModifier((int)(100.0 * computed), (float)roughInsertionCountDelaunayPointCountTerm);
		InsertOptional(progressReporter);
		if (reporter.CancelRequested)
		{
			return null;
		}
		ExecuteAfterAllInsertions();
		EnforceMaxLengthConstraintForInternalEdges();
		EnforceBreakLineEdges();
		EnforcePerimeterEdges();
		FlagDeletedTriangles();
		if (_normalizedOffset != Vector.Zero)
		{
			Vector vec = _normalizedOffset.Negate();
			_tin.Translate(vec);
		}
		if (!KeepDeletedTriangles)
		{
			_tin.RemoveDeletedTriangles();
		}
		return _tin;
	}

	public bool CanComputeFromExistingTin(Tin existingTin)
	{
		Extent other = ComputeMaximalExtent();
		return existingTin.Extent.ContainsOrEquals(other);
	}

	public Tin ComputeTINFromExistingTin(Tin existingTin, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (!ValidateMessages(out var errMsg))
		{
			reporter.ReportError(errMsg);
			throw new Exception(errMsg);
		}
		bool hasZ = _polygons.SelectMany((ComplexPolygon cp) => cp.Rings).Any((Polygon ring) => ring.HasZ) || _delaunayBreaklines.Any((Polyline bl) => bl.HasZ) || _delaunayPoints.HasZ;
		bool hasM = _polygons.SelectMany((ComplexPolygon cp) => cp.Rings).Any((Polygon ring) => ring.HasM) || _delaunayBreaklines.Any((Polyline bl) => bl.HasM) || _delaunayPoints.HasM;
		if (hasZ != existingTin.HasZ || hasM != existingTin.HasM)
		{
			throw new ArgumentException("Mismatching HasZ or HasM data.");
		}
		_tin = existingTin.Clone();
		if (hasZ)
		{
			_tin.TrackZ();
		}
		if (hasM)
		{
			_tin.TrackM();
		}
		double delaunayPointCount = ((_delaunayPoints != null) ? _delaunayPoints.Count : 0);
		double roughInsertionCount = GetRoughInsertionCount();
		double roughInsertionCountDelaunayPointCountTerm = roughInsertionCount / (roughInsertionCount + delaunayPointCount);
		if (double.IsNaN(roughInsertionCountDelaunayPointCountTerm))
		{
			roughInsertionCountDelaunayPointCountTerm = 0.0;
		}
		double computed = 1.0 - roughInsertionCountDelaunayPointCountTerm;
		Extent extent = ComputeMaximalExtent();
		if (!existingTin.Extent.ContainsOrEquals(extent))
		{
			throw new InvalidOperationException("Cannot re-use a tin that has a smaller maximal extent than the new tin.");
		}
		if (TriangulationParameters.Tolerance.HasValue)
		{
			_nearPointTolerance = TriangulationParameters.Tolerance.Value;
		}
		else
		{
			_nearPointTolerance = TinFactory.ComputeNearPointTolerance(extent);
		}
		double scaledNearPointTolerance = _nearPointTolerance * 10.0;
		extent = extent.EnforceMinDimensions(scaledNearPointTolerance, scaledNearPointTolerance).EnforceMinDXDYRatio(0.125);
		if (TriangulationParameters.EnforcedMBR.HasValue)
		{
			extent = extent.Union(TriangulationParameters.EnforcedMBR.Value);
		}
		if (!TriangulationParameters.NormalizedOffset.HasValue)
		{
			_normalizedOffset = ComputeOffset(extent);
		}
		else
		{
			_normalizedOffset = TriangulationParameters.NormalizedOffset.Value;
		}
		if (_normalizedOffset != Vector.Zero)
		{
			_tin.Translate(_normalizedOffset);
		}
		InsertPerimeterPoints();
		InsertBreaklinePoints();
		ProgressReporter progressReporter = null;
		if (_delaunayPoints.Count > 0)
		{
			progressReporter = reporter.WithProgressModifier(0, (float)computed);
			DelaunayInsertPoints(_delaunayPoints, translatePoint: true, progressReporter);
		}
		progressReporter = reporter.WithProgressModifier((int)(100.0 * computed), (float)roughInsertionCountDelaunayPointCountTerm);
		InsertOptional(progressReporter);
		if (reporter.CancelRequested)
		{
			return null;
		}
		ExecuteAfterAllInsertions();
		EnforceMaxLengthConstraintForInternalEdges();
		EnforceBreakLineEdges();
		EnforcePerimeterEdges();
		FlagDeletedTriangles();
		if (_normalizedOffset != Vector.Zero)
		{
			Vector vec = _normalizedOffset.Negate();
			_tin.Translate(vec);
		}
		if (!KeepDeletedTriangles)
		{
			_tin.RemoveDeletedTriangles();
		}
		return _tin;
	}

	public virtual bool ValidateMessages(out string errMsg)
	{
		if (_polygons.Count == 0 && _delaunayBreaklines.Count == 0 && _delaunayPoints.Count == 0)
		{
			errMsg = "No triangulation inputs provided.";
			return false;
		}
		foreach (ComplexPolygon polygon in _polygons)
		{
			if (!polygon.IsValid)
			{
				errMsg = "Complex Polygon invalid.";
				return false;
			}
		}
		foreach (Polyline delaunayBreakline in _delaunayBreaklines)
		{
			if (!delaunayBreakline.IsValidPolyline())
			{
				errMsg = "Invalid Breakline.";
				return false;
			}
		}
		errMsg = "";
		return true;
	}

	protected virtual long GetRoughInsertionCount()
	{
		return 0L;
	}

	protected virtual void InsertOptional(ProgressReporter reporter)
	{
	}

	protected virtual void ExecuteAfterAllInsertions()
	{
	}

	private void AddOctagonPointsAndTriangles(Extent extent)
	{
		Extent extent2 = extent.Translate(_normalizedOffset);
		double widthExtent2Term = Math.Max(extent2.Width, extent2.Height) * 0.05;
		_tin.Add(extent2.UpperMidside.Translate(0.0, widthExtent2Term * 2.0));
		_tin.Add(extent2.UpperRight.Translate(widthExtent2Term, widthExtent2Term));
		_tin.Add(extent2.RightMidside.Translate(widthExtent2Term * 2.0, 0.0));
		_tin.Add(extent2.LowerRight.Translate(widthExtent2Term, 0.0 - widthExtent2Term));
		_tin.Add(extent2.LowerMidside.Translate(0.0, (0.0 - widthExtent2Term) * 2.0));
		_tin.Add(extent2.LowerLeft.Translate(0.0 - widthExtent2Term, 0.0 - widthExtent2Term));
		_tin.Add(extent2.LeftMidside.Translate((0.0 - widthExtent2Term) * 2.0, 0.0));
		_tin.Add(extent2.UpperLeft.Translate(0.0 - widthExtent2Term, widthExtent2Term));
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
		double maxXAboveMinX = extent2.MaxX - extent2.MinX;
		double maxYAboveMinY = extent2.MaxY - extent2.MinY;
		_tin.Add(extent2.LowerRight.Translate(1.0 * maxXAboveMinX, -1.0 * maxYAboveMinY));
		_tin.Add(extent2.UpperLeft.Translate(0.5 * maxXAboveMinX, 1.5 * maxYAboveMinY));
		_tin.Add(extent2.LowerLeft.Translate(-1.0 * maxXAboveMinX, -1.0 * maxYAboveMinY));
		AddTriangle(0, 1, 2, -1, -1, -1);
	}

	protected bool IsSuperPoint(int idx)
	{
		return idx < 8;
	}

	protected bool IsSuperTriangle(int t)
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[t];
		if (triangleDefinition.p0 >= 8 && triangleDefinition.p1 >= 8)
		{
			return triangleDefinition.p2 < 8;
		}
		return true;
	}

	protected virtual Extent ComputeMaximalExtent()
	{
		Extent extent = Extent.NullExtent;
		foreach (ComplexPolygon polygon in _polygons)
		{
			foreach (Polygon ring in polygon.Rings)
			{
				extent = Extent.Union(extent, ring.Extent);
			}
		}
		foreach (Polyline delaunayBreakline in _delaunayBreaklines)
		{
			extent = Extent.Union(extent, delaunayBreakline.Extent);
		}
		if (!_delaunayPoints.IsNullOrEmpty())
		{
			extent = Extent.Union(extent, _delaunayPoints.Extent);
		}
		return extent;
	}

	private static Vector ComputeOffset(Extent extent)
	{
		return extent.LowerLeft.To(0.0, 0.0);
	}

	protected void DelaunayInsertPoints(PointCollection points, bool translatePoint, ProgressReporter rep)
	{
		int t = 0;
		int count = points.Count;
		for (int i = 0; i < count; i++)
		{
			int p3;
			if (_tin.HasZ && _tin.HasM)
			{
				if (!DelaunayInsertPoint(points.Point4D[i], ref t, translatePoint, out var _))
				{
					_markDupes?.Invoke(i);
				}
			}
			else if (_tin.HasZ)
			{
				if (!DelaunayInsertPoint(points.Point3D[i], ref t, translatePoint, out var _))
				{
					_markDupes?.Invoke(i);
				}
			}
			else if (!DelaunayInsertPoint(points[i], ref t, translatePoint, out p3))
			{
				_markDupes?.Invoke(i);
			}
			if (i % 1000 == 0)
			{
				rep?.ReportProgress((int)(100.0 * ((double)i / (double)points.Count)));
			}
		}
	}

	protected void DelaunayInsertPoints_UpdateBreakline(ref Polyline pline)
	{
		int reft = 0;
		int count = pline.Count;
		bool isOriginal = true;
		InsertUpdatePoint(0, ref pline);
		InsertUpdatePoint(pline.Count - 1, ref pline);
		for (int i = 1; i < count - 1; i++)
		{
			InsertUpdatePoint(i, ref pline);
		}
		if (!isOriginal)
		{
			pline.ForceIncrementVersion();
		}
		void InsertUpdatePoint(int ordinal, ref Polyline ppl)
		{
			Memory<Point> pointBufferUnsafe;
			Memory<double> doubleSpan;
			int p3;
			if (_tin.HasZ && _tin.HasM)
			{
				if (!DelaunayInsertPoint(ppl.Point4D[ordinal], ref reft, translatePoint: true, out var p))
				{
					if (isOriginal)
					{
						ppl = VClone(_tin, ppl);
						isOriginal = false;
					}
					pointBufferUnsafe = ppl.GetPointBufferUnsafe();
					pointBufferUnsafe.Span[ordinal] = Tin.Points[p].Translate(-_normalizedOffset);
					doubleSpan = ppl.GetZBufferUnsafe();
					doubleSpan.Span[ordinal] = Tin.Z[p];
					doubleSpan = ppl.GetMBufferUnsafe();
					doubleSpan.Span[ordinal] = Tin.M[p];
					_markDupes?.Invoke(ordinal);
				}
			}
			else if (_tin.HasZ)
			{
				if (!DelaunayInsertPoint(ppl.Point3D[ordinal], ref reft, translatePoint: true, out var p2))
				{
					if (isOriginal)
					{
						ppl = VClone(_tin, ppl);
						isOriginal = false;
					}
					pointBufferUnsafe = ppl.GetPointBufferUnsafe();
					pointBufferUnsafe.Span[ordinal] = Tin.Points[p2].Translate(-_normalizedOffset);
					doubleSpan = ppl.GetZBufferUnsafe();
					doubleSpan.Span[ordinal] = Tin.Z[p2];
					_markDupes?.Invoke(ordinal);
				}
			}
			else if (!DelaunayInsertPoint(ppl[ordinal], ref reft, translatePoint: true, out p3))
			{
				if (isOriginal)
				{
					ppl = VClone(_tin, ppl);
					isOriginal = false;
				}
				pointBufferUnsafe = ppl.GetPointBufferUnsafe();
				pointBufferUnsafe.Span[ordinal] = Tin.Points[p3].Translate(-_normalizedOffset);
				_markDupes?.Invoke(ordinal);
			}
		}
		static Polyline VClone(Tin t, Polyline p)
		{
			Polyline polyline = null;
			polyline = ((!(p is Polygon polygon)) ? p.Clone() : polygon.Clone());
			if (t.HasZ)
			{
				polyline.TrackZ();
			}
			if (t.HasM)
			{
				polyline.TrackM();
			}
			return polyline;
		}
	}

	protected void DelaunayInsertPoints(Memory<Point> points, bool translatePoint)
	{
		int t = 0;
		Span<Point> pointsSpan = points.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			if (!DelaunayInsertPoint(pointsSpan[i], ref t, translatePoint, out var _))
			{
				_markDupes?.Invoke(i);
			}
		}
	}

	protected bool DelaunayInsertPoint(Point pt, ref int t, bool translatePoint, out int p)
	{
		if (translatePoint)
		{
			pt = pt.Translate(_normalizedOffset);
		}
		_tin.FindTriangleCheckNearbyCorners(pt, _nearPointTolerance, ref t, out p);
		if (p >= 0)
		{
			return false;
		}
		DelaunayInsert(t, pt);
		return true;
	}

	protected bool DelaunayInsertPoint(Point3D pt, ref int t, bool translatePoint, out int p)
	{
		if (translatePoint)
		{
			pt = pt.Translate(_normalizedOffset);
		}
		_tin.FindTriangleCheckNearbyCorners(pt.AsPoint2D(), _nearPointTolerance, ref t, out p);
		if (p >= 0)
		{
			_tin.UpdatePointZ(p, pt);
			return false;
		}
		DelaunayInsert(t, pt);
		return true;
	}

	protected bool DelaunayInsertPoint(Point4D pt, ref int t, bool translatePoint, out int p)
	{
		if (translatePoint)
		{
			pt = pt.Translate(_normalizedOffset);
		}
		_tin.FindTriangleCheckNearbyCorners(pt.AsPoint2D(), _nearPointTolerance, ref t, out p);
		if (p >= 0)
		{
			_tin.UpdatePointZM(p, pt);
			return false;
		}
		DelaunayInsert(t, pt);
		return true;
	}

	private void DelaunayInsert(int triIdx, Point pt)
	{
		_tin.Add(pt);
		DelaunayRetriangulateInternal(pt, triIdx);
	}

	private void DelaunayInsert(int triIdx, Point3D ptZ)
	{
		_tin.Add(ptZ);
		DelaunayRetriangulateInternal(ptZ.AsPoint2D(), triIdx);
	}

	private void DelaunayInsert(int triIdx, Point4D ptZM)
	{
		_tin.Add(ptZM);
		DelaunayRetriangulateInternal(ptZM.AsPoint2D(), triIdx);
	}

	private void DelaunayRetriangulateInternal(Point pt, int triIdx)
	{
		Stack<int> trisToDeleteStack = _trisToDeleteStack;
		trisToDeleteStack.Push(triIdx);
		while (trisToDeleteStack.Count != 0)
		{
			triIdx = trisToDeleteStack.Pop();
			_deletedTrianglesStack.Push(triIdx);
			MarkDeleted(triIdx);
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[triIdx];
			if (IsTriangleInsideInsertionHole(triangleDefinition.at0, pt))
			{
				trisToDeleteStack.Push(triangleDefinition.at0);
			}
			if (IsTriangleInsideInsertionHole(triangleDefinition.at1, pt))
			{
				trisToDeleteStack.Push(triangleDefinition.at1);
			}
			if (IsTriangleInsideInsertionHole(triangleDefinition.at2, pt))
			{
				trisToDeleteStack.Push(triangleDefinition.at2);
			}
		}
		MakeInsertionHoleFromDeletedTrianglesAndFill(_tin.Count - 1);
	}

	private List<InsertionHoleEdge> MakeInsertionHoleFromDeletedTrianglesAndFill(int p)
	{
		_insertionEdges.Clear();
		List<InsertionHoleEdge> insertionEdges = _insertionEdges;
		foreach (int deletedTrianglesStack in _deletedTrianglesStack)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[deletedTrianglesStack];
			if (triangleDefinition.at0 == -1 || !_tin.TriangleDefinition[triangleDefinition.at0].IsDeleted)
			{
				insertionEdges.Add(new InsertionHoleEdge(triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.at0, -1));
			}
			if (triangleDefinition.at1 == -1 || !_tin.TriangleDefinition[triangleDefinition.at1].IsDeleted)
			{
				insertionEdges.Add(new InsertionHoleEdge(triangleDefinition.p1, triangleDefinition.p2, triangleDefinition.at1, -1));
			}
			if (triangleDefinition.at2 == -1 || !_tin.TriangleDefinition[triangleDefinition.at2].IsDeleted)
			{
				insertionEdges.Add(new InsertionHoleEdge(triangleDefinition.p2, triangleDefinition.p0, triangleDefinition.at2, -1));
			}
		}
		_insertionHolePoints.Clear();
		Dictionary<int, InsertionHolePointTriangles> insertionHolePoints = _insertionHolePoints;
		for (int i = 0; i < insertionEdges.Count; i++)
		{
			InsertionHoleEdge insertionEdge = insertionEdges[i];
			int insideTriangleInsertionEdgeTerm = (insertionEdge.InsideTriangle = AddTriangle(insertionEdge.p0, insertionEdge.p1, p, insertionEdge.OutsideTriangle, -1, -1));
			insertionEdges[i] = insertionEdge;
			bool tryGetValue = insertionHolePoints.TryGetValue(insertionEdge.p0, out var insertionHolePoints2);
			bool tryGetValue2 = insertionHolePoints.TryGetValue(insertionEdge.p1, out var insertionHolePoints3);
			insertionHolePoints2.RightTriangle = insideTriangleInsertionEdgeTerm;
			insertionHolePoints3.LeftTriangle = insideTriangleInsertionEdgeTerm;
			if (tryGetValue)
			{
				insertionHolePoints[insertionEdge.p0] = insertionHolePoints2;
			}
			else
			{
				insertionHolePoints.Add(insertionEdge.p0, insertionHolePoints2);
			}
			if (tryGetValue2)
			{
				insertionHolePoints[insertionEdge.p1] = insertionHolePoints3;
			}
			else
			{
				insertionHolePoints.Add(insertionEdge.p1, insertionHolePoints3);
			}
			if (insertionEdge.OutsideTriangle >= 0)
			{
				TriangleDefinition triangleDefinition2 = _tin.TriangleDefinition[insertionEdge.OutsideTriangle];
				if (triangleDefinition2.p0 == insertionEdge.p1)
				{
					triangleDefinition2.at0 = insideTriangleInsertionEdgeTerm;
				}
				else if (triangleDefinition2.p1 == insertionEdge.p1)
				{
					triangleDefinition2.at1 = insideTriangleInsertionEdgeTerm;
				}
				else if (triangleDefinition2.p2 == insertionEdge.p1)
				{
					triangleDefinition2.at2 = insideTriangleInsertionEdgeTerm;
				}
				_tin.TriangleDefinition[insertionEdge.OutsideTriangle] = triangleDefinition2;
			}
		}
		foreach (InsertionHoleEdge insertionEdge2 in insertionEdges)
		{
			TriangleDefinition triangleDefinition3 = _tin.TriangleDefinition[insertionEdge2.InsideTriangle];
			triangleDefinition3.at1 = insertionHolePoints[insertionEdge2.p1].RightTriangle;
			triangleDefinition3.at2 = insertionHolePoints[insertionEdge2.p0].LeftTriangle;
			_tin.TriangleDefinition[insertionEdge2.InsideTriangle] = triangleDefinition3;
		}
		return insertionEdges;
	}

	protected bool IsTriangleInsideInsertionHole(int at, Point point)
	{
		return IsTriangleInsideInsertionHole<NativePredicates>(at, point);
	}

	protected bool IsTriangleInsideInsertionHole<Tpred>(int at, Point point) where Tpred : struct, IPredicates
	{
		if (at == -1)
		{
			return false;
		}
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[at];
		if (triangleDefinition.IsDeleted)
		{
			return false;
		}
		return Tpred.InCircle(_tin.Points[triangleDefinition.p0], _tin.Points[triangleDefinition.p1], _tin.Points[triangleDefinition.p2], point) >= 0.0;
	}

	protected void EnforceEdgeSegments(IReadOnlyList<Segment4D> segments, bool isPerimeter, int enforcedFromIdx, ref int reft)
	{
		int count = segments.Count;
		for (int i = 0; i < count; i++)
		{
			if (segments[i].Length2D != 0.0)
			{
				EnforceEdgeSegment(segments[i], isPerimeter, enforcedFromIdx, ref reft);
			}
		}
	}

	protected void EnforceEdgeSegment(Segment4D segment, bool isPerimeter, int enforcedFromIdx, ref int reft)
	{
		if (_retriangulateConstrainedFace)
		{
			EnforceEdgeSegmentRetriangulate(segment, isPerimeter, enforcedFromIdx, ref reft);
		}
		else
		{
			EnforceEdgeSegmentSplitting(segment, isPerimeter, enforcedFromIdx, ref reft);
		}
	}

	private static Dictionary<int, (int, int)> ComputeConstraintReverseLookup(Dictionary<(int, int), int> lookup)
	{
		Dictionary<int, (int, int)> constraintReverseLookup = new Dictionary<int, (int, int)>();
		foreach (KeyValuePair<(int, int), int> lookup2 in lookup)
		{
			constraintReverseLookup.TryAdd(lookup2.Value, lookup2.Key);
		}
		return constraintReverseLookup;
	}

	protected override void SetConstrainedEdge(ConstrainedEdge constraint)
	{
		if (constraint.PointA == constraint.PointB)
		{
			throw new Exception("Cannot constrain an edge with the same PA/PB");
		}
		base.SetConstrainedEdge(constraint);
		(int, int) key = constraint.SortedPointPair();
		if (constraint.IsPerimeter)
		{
			if (OnEdgeOverConstrained != null && _perimConstraints.TryGetValue(key, out var localValue))
			{
				ConstrainedEdge constrainedEdge = new ConstrainedEdge(key.Item1, key.Item2, -1, isPerim: true, localValue);
				OnEdgeOverConstrained(constrainedEdge, constraint);
				if (TriangulationParameters.ConstrainedEdgeMode == ConstrainedEdgeMode.UseFirstConstraining)
				{
					return;
				}
			}
			if (TriangulationParameters.ConstrainedEdgeMode == ConstrainedEdgeMode.UseFirstConstraining)
			{
				_perimConstraints.TryAdd(key, constraint.EnforcedFromIndex);
			}
			else
			{
				_perimConstraints[key] = constraint.EnforcedFromIndex;
			}
			return;
		}
		if (OnEdgeOverConstrained != null && _blineConstraints.TryGetValue(key, out var localValue2))
		{
			ConstrainedEdge constrainedEdge2 = new ConstrainedEdge(key.Item1, key.Item2, -1, isPerim: false, localValue2);
			OnEdgeOverConstrained(constrainedEdge2, constraint);
			if (TriangulationParameters.ConstrainedEdgeMode == ConstrainedEdgeMode.UseFirstConstraining)
			{
				return;
			}
		}
		if (TriangulationParameters.ConstrainedEdgeMode == ConstrainedEdgeMode.UseFirstConstraining)
		{
			_blineConstraints.TryAdd(key, constraint.EnforcedFromIndex);
		}
		else
		{
			_blineConstraints[key] = constraint.EnforcedFromIndex;
		}
	}

	public bool IsConstrained(int pA, int pB, out bool isPerimeter, out int constraintIdx)
	{
		(int, int) key = ((pA < pB) ? (pA, pB) : (pB, pA));
		if (_perimConstraints.TryGetValue(key, out var constraintIdx2))
		{
			isPerimeter = true;
			constraintIdx = constraintIdx2;
			return true;
		}
		if (_blineConstraints.TryGetValue(key, out constraintIdx2))
		{
			isPerimeter = false;
			constraintIdx = constraintIdx2;
			return true;
		}
		isPerimeter = false;
		constraintIdx = -1;
		return false;
	}

	public bool IsConstrained(int pA, int pB)
	{
		bool isPerimeter;
		int constraintIdx;
		return IsConstrained(pA, pB, out isPerimeter, out constraintIdx);
	}

	public bool FindConstrainedEdge(int constraintIdx, bool checkPerimEdges, bool checkBlineEdges, out (int, int) pointPair)
	{
		if (checkPerimEdges && PerimConstraintsReverseLookup.TryGetValue(constraintIdx, out pointPair))
		{
			return true;
		}
		if (checkBlineEdges && BreaklineConstraintsReverseLookup.TryGetValue(constraintIdx, out pointPair))
		{
			return true;
		}
		pointPair = (-1, -1);
		return false;
	}

	protected void EnforceEdgeSegmentSplitting(Segment4D segment, bool isPerimeter, int enforceIdx, ref int reft)
	{
		segment = segment.Translate(_normalizedOffset);
		_tin.FindTriangleAndNearestPointIndex(segment.B.AsPoint2D(), ref reft, out var p);
		segment.B = _tin.Point4D[p];
		int p2;
		while (true)
		{
			_tin.FindTriangleAndNearestPointIndex(segment.A.AsPoint2D(), ref reft, out p2);
			segment.A = _tin.Point4D[p2];
			Segment segment2 = segment.AsSegment2D();
			SpinFindTriangleToBisect(segment2, p2, ref reft, out var triA, out var e);
			Segment seg = _tin.Edge(reft, e);
			if (triA.p0 == p || triA.p1 == p || triA.p2 == p)
			{
				break;
			}
			segment2.Intersects(in seg, out var _, out var _, out var t, out var _, 0.0);
			Point4D point4D = segment.PointAtFraction(t);
			Point point = point4D.AsPoint2D();
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[reft];
			int p22 = triangleDefinition.EdgePointA(e);
			int p23 = triangleDefinition.EdgePointB(e);
			if (point.CloseToXY(seg.A, _nearPointTolerance))
			{
				SetConstrainedEdge(new ConstrainedEdge(p2, p22, reft, isPerimeter, enforceIdx));
				p2 = p22;
				_tin.UpdatePointZM(p2, point4D);
				segment.A = _tin.Point4D[p2];
				continue;
			}
			if (point.CloseToXY(seg.B, _nearPointTolerance))
			{
				SetConstrainedEdge(new ConstrainedEdge(p2, p23, reft, isPerimeter, enforceIdx));
				p2 = p23;
				_tin.UpdatePointZM(p2, point4D);
				segment.A = _tin.Point4D[p2];
				continue;
			}
			int pointCount = _tin.PointCount;
			_tin.Add(point4D);
			int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(e);
			_deletedTrianglesStack.Push(reft);
			_deletedTrianglesStack.Push(adjacentTriangleIdx);
			MarkDeleted(reft);
			MarkDeleted(adjacentTriangleIdx);
			MakeInsertionHoleFromDeletedTrianglesAndFill(pointCount);
			if (IsConstrained(p22, p23, out var isPerimeter2, out var constraintIdx))
			{
				(int, int) key = ((p22 < p23) ? (p22, p23) : (p23, p22));
				_blineConstraints.Remove(key);
				_perimConstraints.Remove(key);
				SetConstrainedEdge(new ConstrainedEdge(p22, pointCount, -1, isPerimeter2, constraintIdx));
				SetConstrainedEdge(new ConstrainedEdge(p23, pointCount, -1, isPerimeter2, constraintIdx));
			}
			SetConstrainedEdge(new ConstrainedEdge(p2, pointCount, -1, isPerimeter, enforceIdx));
			segment.A = point4D;
		}
		if (p2 != p)
		{
			SetConstrainedEdge(new ConstrainedEdge(p2, p, reft, isPerimeter, enforceIdx));
		}
	}

	protected virtual void EnforceEdgeSegmentRetriangulate(Segment4D segment, bool isPerimeter, int enforceIdx, ref int reft, TinEditLog log = null)
	{
		segment = segment.Translate(_normalizedOffset);
		_tin.FindTriangleAndNearestPointIndex(segment.B.AsPoint2D(), ref reft, out var p);
		_tin.FindTriangleAndNearestPointIndex(segment.A.AsPoint2D(), ref reft, out var p2);
		if (p2 != p)
		{
			EnforceEdgeSegmentRetriangulateInternal(p2, p, reft, isPerimeter, enforceIdx, log);
		}
	}

	protected void EnforceEdgeSegmentRetriangulate(int pA, int pB, bool isPerimeter, int enforceIdx, TinEditLog log = null)
	{
		int t = 0;
		EnforceEdgeSegmentRetriangulate(pA, pB, isPerimeter, enforceIdx, ref t, log);
	}

	protected void EnforceEdgeSegmentRetriangulate(int pA, int pB, bool isPerimeter, int enforceIdx, ref int t, TinEditLog log = null)
	{
		if (t < 0)
		{
			t = 0;
		}
		_tin.FindTriangleAndNearestPointIndex(_tin.Points[pA], ref t, out var _);
		EnforceEdgeSegmentRetriangulateInternal(pA, pB, t, isPerimeter, enforceIdx, log);
	}

	protected virtual void EnforceEdgeSegmentRetriangulateInternal(int pA, int pB, int t, bool isPerimeter, int enforceIdx, TinEditLog log = null)
	{
		EnforceEdgeSegmentRetriangulateInternal<NativePredicates>(pA, pB, t, isPerimeter, enforceIdx, log);
	}

	protected void EnforceEdgeSegmentRetriangulateInternal<Tpred>(int pA, int pB, int t, bool isPerimeter, int enforceIdx, TinEditLog log = null) where Tpred : struct, IPredicates
	{
		Segment segment = new Segment(_tin.Points[pA], _tin.Points[pB]);
		SpinFindTriangleToBisect<Tpred>(segment, pA, ref t, out var triA, out var e);
		if (triA.p0 == pB || triA.p1 == pB || triA.p2 == pB)
		{
			SetConstrainedEdge(new ConstrainedEdge(pA, pB, t, isPerimeter, enforceIdx));
			return;
		}
		List<int> intList = new List<int> { pA };
		List<int> intListList = new List<int> { pA };
		Dictionary<Tuple<int, int>, int> intByValueTuple = new Dictionary<Tuple<int, int>, int>();
		int adjacentTriangleIdxTriATerm;
		int adjacentTriangleIdxTriATerm2;
		while (true)
		{
			int edgePointA = triA.EdgePointA(e);
			int edgePointB = triA.EdgePointB(e);
			if (OnEdgeOverConstrained != null && IsConstrained(edgePointB, edgePointA, out var isPerimeter2, out var constraintIdx))
			{
				OnEdgeOverConstrained(new ConstrainedEdge(edgePointA, edgePointB, -1, isPerimeter2, constraintIdx), new ConstrainedEdge(pA, pB, -1, isPerimeter, enforceIdx));
			}
			if (intListList.Last() != edgePointA)
			{
				adjacentTriangleIdxTriATerm = triA.AdjacentTriangleIdx((e + 2) % 3);
				int ordinal = intListList.Last();
				int edgePointA2 = edgePointA;
				if (intByValueTuple.ContainsKey(new Tuple<int, int>(edgePointA2, ordinal)))
				{
					adjacentTriangleIdxTriATerm = -1;
					intByValueTuple[new Tuple<int, int>(edgePointA2, ordinal)] = -1;
				}
				intByValueTuple.Add(new Tuple<int, int>(ordinal, edgePointA2), adjacentTriangleIdxTriATerm);
				intListList.Add(edgePointA);
			}
			if (intList.Last() != edgePointB)
			{
				adjacentTriangleIdxTriATerm2 = triA.AdjacentTriangleIdx((e + 1) % 3);
				int edgePointB2 = edgePointB;
				int ordinal2 = intList.Last();
				if (intByValueTuple.ContainsKey(new Tuple<int, int>(ordinal2, edgePointB2)))
				{
					adjacentTriangleIdxTriATerm2 = -1;
					intByValueTuple[new Tuple<int, int>(ordinal2, edgePointB2)] = -1;
				}
				intByValueTuple.Add(new Tuple<int, int>(edgePointB2, ordinal2), adjacentTriangleIdxTriATerm2);
				intList.Add(edgePointB);
			}
			_deletedTrianglesStack.Push(t);
			MarkDeleted(t);
			log?.LogDeleteEdge(edgePointA, edgePointB);
			t = triA.AdjacentTriangleIdx(e);
			triA = _tin.TriangleDefinition[t];
			e = triA.GetEdge012(edgePointB, edgePointA);
			int adjacentPointIdx = triA.AdjacentPointIdx((e + 2) % 3);
			if (adjacentPointIdx == pB)
			{
				break;
			}
			double counterClockwise = Tpred.CounterClockwise(_tin.Points[pA], _tin.Points[pB], _tin.Points[adjacentPointIdx]);
			if (counterClockwise > 0.0)
			{
				e = (e + 1) % 3;
			}
			else
			{
				e = ((!(counterClockwise < 0.0)) ? ((e + 1) % 3) : ((e + 2) % 3));
			}
		}
		_deletedTrianglesStack.Push(t);
		MarkDeleted(t);
		if (log != null)
		{
			foreach (int deletedTrianglesStack in _deletedTrianglesStack)
			{
				log.LogDeleteTriangle(_tin.TriangleDefinition[deletedTrianglesStack]);
			}
		}
		adjacentTriangleIdxTriATerm = triA.AdjacentTriangleIdx((e + 1) % 3);
		intByValueTuple.Add(new Tuple<int, int>(intListList.Last(), pB), adjacentTriangleIdxTriATerm);
		intListList.Add(pB);
		adjacentTriangleIdxTriATerm2 = triA.AdjacentTriangleIdx((e + 2) % 3);
		intByValueTuple.Add(new Tuple<int, int>(pB, intList.Last()), adjacentTriangleIdxTriATerm2);
		intList.Add(pB);
		int tpredPBTerm = TriangulatePseudoPolygon<Tpred>(pB, pA, intListList, intByValueTuple, log);
		intList.Reverse();
		int tpredPATerm = TriangulatePseudoPolygon<Tpred>(pA, pB, intList, intByValueTuple, log);
		log?.LogAddEdge(pA, pB);
		triA = _tin.TriangleDefinition[tpredPBTerm];
		triA.SetAdjacentTriangle(pB, pA, tpredPATerm);
		_tin.TriangleDefinition[tpredPBTerm] = triA;
		triA = _tin.TriangleDefinition[tpredPATerm];
		triA.SetAdjacentTriangle(pB, pA, tpredPBTerm);
		_tin.TriangleDefinition[tpredPATerm] = triA;
		SetConstrainedEdge(new ConstrainedEdge(pA, pB, tpredPBTerm, isPerimeter, enforceIdx));
	}

	protected int TriangulatePseudoPolygon(int pA, int pB, List<int> holeBoundary, Dictionary<Tuple<int, int>, int> holeAdjacent, TinEditLog log = null, int hIdxStart = -1, int hIdxStop = -1)
	{
		return TriangulatePseudoPolygon<NativePredicates>(pA, pB, holeBoundary, holeAdjacent, log, hIdxStart, hIdxStop);
	}

	protected int TriangulatePseudoPolygon<Tpred>(int pA, int pB, List<int> holeBoundary, Dictionary<Tuple<int, int>, int> holeAdjacent, TinEditLog log = null, int hIdxStart = -1, int hIdxStop = -1) where Tpred : struct, IPredicates
	{
		if (hIdxStart < 0)
		{
			hIdxStart = 1;
		}
		if (hIdxStop < 0)
		{
			hIdxStop = holeBoundary.Count - 1;
		}
		if (hIdxStop == hIdxStart)
		{
			return holeAdjacent[new Tuple<int, int>(pB, pA)];
		}
		int hIdxStart2 = hIdxStart;
		int holeBoundary2 = holeBoundary[hIdxStart2];
		for (int i = hIdxStart + 1; i < hIdxStop; i++)
		{
			int holeBoundary3 = holeBoundary[i];
			if (Tpred.InCircle(_tin.Points[pA], _tin.Points[pB], _tin.Points[holeBoundary2], _tin.Points[holeBoundary3]) > 0.0)
			{
				hIdxStart2 = i;
				holeBoundary2 = holeBoundary3;
			}
		}
		int tpredHoleBoundary2Term = TriangulatePseudoPolygon<Tpred>(holeBoundary2, pB, holeBoundary, holeAdjacent, log, hIdxStart, hIdxStart2);
		int tpredPATerm = TriangulatePseudoPolygon<Tpred>(pA, holeBoundary2, holeBoundary, holeAdjacent, log, hIdxStart2 + 1, hIdxStop);
		int holeAdjacent2 = AddTriangle(pA, pB, holeBoundary2, -1, tpredHoleBoundary2Term, tpredPATerm);
		if (log != null)
		{
			if (hIdxStart2 > hIdxStart)
			{
				log.LogAddEdge(pB, holeBoundary2);
			}
			if (hIdxStart2 < hIdxStop - 1)
			{
				log.LogAddEdge(pA, holeBoundary2);
			}
			log.LogAddTriangle(_tin.TriangleDefinition[holeAdjacent2]);
		}
		if (tpredHoleBoundary2Term >= 0)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[tpredHoleBoundary2Term];
			triangleDefinition.SetAdjacentTriangle(pB, holeBoundary2, holeAdjacent2);
			_tin.TriangleDefinition[tpredHoleBoundary2Term] = triangleDefinition;
		}
		else if (holeBoundary2 >= 8 || pB >= 8)
		{
			Tuple<int, int> key = new Tuple<int, int>(holeBoundary2, pB);
			holeAdjacent[key] = holeAdjacent2;
		}
		if (tpredPATerm >= 0)
		{
			TriangleDefinition triangleDefinition2 = _tin.TriangleDefinition[tpredPATerm];
			triangleDefinition2.SetAdjacentTriangle(holeBoundary2, pA, holeAdjacent2);
			_tin.TriangleDefinition[tpredPATerm] = triangleDefinition2;
		}
		else if (pA >= 8 || holeBoundary2 >= 8)
		{
			Tuple<int, int> key2 = new Tuple<int, int>(pA, holeBoundary2);
			holeAdjacent[key2] = holeAdjacent2;
		}
		return holeAdjacent2;
	}

	protected void SpinFindTriangleToBisect(Segment segment, int pA, ref int tA, out TriangleDefinition triA, out int e012)
	{
		SpinFindTriangleToBisect<NativePredicates>(segment, pA, ref tA, out triA, out e012);
	}

	protected void SpinFindTriangleToBisect<Tpred>(Segment segment, int pA, ref int tA, out TriangleDefinition triA, out int e012) where Tpred : struct, IPredicates
	{
		while (true)
		{
			triA = _tin.TriangleDefinition[tA];
			int tA2;
			int tA3;
			if (pA == triA.p0)
			{
				e012 = 1;
				tA2 = triA.at0;
				tA3 = triA.at2;
			}
			else if (pA == triA.p1)
			{
				e012 = 2;
				tA2 = triA.at1;
				tA3 = triA.at0;
			}
			else
			{
				e012 = 0;
				tA2 = triA.at2;
				tA3 = triA.at1;
			}
			Segment segment2 = _tin.Edge(tA, e012);
			if (Tpred.CounterClockwise(_tin.Points[pA], segment2.A, segment.B) < 0.0)
			{
				tA = tA2;
				continue;
			}
			if (Tpred.CounterClockwise(_tin.Points[pA], segment2.B, segment.B) > 0.0)
			{
				tA = tA3;
				continue;
			}
			break;
		}
	}

	private void InsertPerimeterPoints()
	{
		if (_polygons.Count == 0)
		{
			return;
		}
		for (int i = 0; i < _polygons.Count; i++)
		{
			PolygonContainer outerRing = _polygons[i].OuterRing;
			outerRing = outerRing.WalkAndModify((Polygon p) =>
			{
				Polyline pline = p;
				DelaunayInsertPoints_UpdateBreakline(ref pline);
				return (pline != p) ? ((Polygon)pline) : null;
			});
			if (outerRing != null)
			{
				_polygons[i] = new ComplexPolygon(outerRing);
			}
		}
		if (double.IsNaN(MaxEdgeLength))
		{
			return;
		}
		for (int intervalIndex = 0; intervalIndex < _polygons.Count; intervalIndex++)
		{
			ComplexPolygon complexPolygon = _polygons[intervalIndex];
			for (int intervalIndex2 = 0; intervalIndex2 < complexPolygon.Rings.Count; intervalIndex2++)
			{
				Polygon polygon = complexPolygon.Rings[intervalIndex2];
				for (int intervalIndex3 = 0; intervalIndex3 < polygon.SegmentCount; intervalIndex3++)
				{
					Point[] internalDensificationPoints = polygon.Segments[intervalIndex3].GetInternalDensificationPoints(MaxEdgeLength);
					if (internalDensificationPoints != null && internalDensificationPoints.Length != 0)
					{
						DelaunayInsertPoints(internalDensificationPoints, translatePoint: true);
					}
				}
			}
		}
	}

	private void InsertBreaklinePoints()
	{
		if (_delaunayBreaklines == null || _delaunayBreaklines.Count == 0)
		{
			return;
		}
		int count = _delaunayBreaklines.Count;
		for (int i = 0; i < count; i++)
		{
			Polyline pline = _delaunayBreaklines[i];
			Polyline polyline = pline;
			DelaunayInsertPoints_UpdateBreakline(ref pline);
			if (polyline != pline)
			{
				_delaunayBreaklines[i] = pline;
			}
			if (double.IsNaN(MaxEdgeLength))
			{
				continue;
			}
			for (int j = 0; j < pline.SegmentCount; j++)
			{
				Point[] internalDensificationPoints = pline.Segments[j].GetInternalDensificationPoints(MaxEdgeLength);
				if (internalDensificationPoints != null && internalDensificationPoints.Length != 0)
				{
					DelaunayInsertPoints(internalDensificationPoints, translatePoint: true);
				}
			}
		}
	}

	private void EnforceMaxLengthConstraintForInternalEdges()
	{
		if (double.IsNaN(MaxEdgeLength))
		{
			return;
		}
		int ordinal;
		do
		{
			ordinal = 0;
			int t = 0;
			for (int i = 0; i < _tin.TriangleDefinition.Count; i++)
			{
				for (int j = 0; j <= 2; j++)
				{
					if (IsSuperPoint(_tin.TriangleDefinition[i].EdgePointA(j)) || IsSuperPoint(_tin.TriangleDefinition[i].EdgePointB(j)))
					{
						continue;
					}
					int p;
					if (HasZ && HasM)
					{
						Segment4D segment4D = _tin.Edge4D(i, j);
						if (segment4D.Length2D > MaxEdgeLength)
						{
							Point4D midPoint = segment4D.MidPoint;
							if (!DelaunayInsertPoint(midPoint, ref t, translatePoint: false, out p))
							{
								break;
							}
							ordinal++;
						}
						continue;
					}
					if (HasZ)
					{
						Segment3D segment3D = _tin.Edge3D(i, j);
						if (segment3D.Length2D > MaxEdgeLength)
						{
							Point3D midPoint2 = segment3D.MidPoint;
							if (!DelaunayInsertPoint(midPoint2, ref t, translatePoint: false, out p))
							{
								break;
							}
							ordinal++;
						}
						continue;
					}
					Segment segment = _tin.Edge(i, j);
					if (segment.Length > MaxEdgeLength)
					{
						Point pt = segment.Midpoint();
						if (!DelaunayInsertPoint(pt, ref t, translatePoint: false, out p))
						{
							break;
						}
						ordinal++;
					}
				}
			}
		}
		while (ordinal != 0);
	}

	private void EnforceBreakLineEdges()
	{
		if (_delaunayBreaklines != null && _delaunayBreaklines.Count != 0)
		{
			int count = _delaunayBreaklines.Count;
			int reft = 0;
			for (int i = 0; i < count; i++)
			{
				Polyline polyline = _delaunayBreaklines[i];
				EnforceEdgeSegments(polyline.Segment4D, isPerimeter: false, i, ref reft);
			}
		}
	}

	private void EnforcePerimeterEdges()
	{
		int reft = 0;
		for (int i = 0; i < _polygons.Count; i++)
		{
			ComplexPolygon complexPolygon = _polygons[i];
			for (int j = 0; j < complexPolygon.Rings.Count; j++)
			{
				Polygon polygon = complexPolygon.Rings[j];
				EnforceEdgeSegments(polygon.Segment4D, isPerimeter: true, i, ref reft);
			}
		}
	}

	protected virtual void FlagDeletedTriangles()
	{
		if (_polygons.Count == 0 || _polygons.All((ComplexPolygon pg) => pg.RingCount == 1))
		{
			FlagDeletedTriangles_SimpleRing();
		}
		else
		{
			MarkBadTrianglesAsDeleted_ComplexRings();
		}
	}

	protected virtual void FlagDeletedTriangles_SimpleRing()
	{
		if (_polygons.Any((ComplexPolygon cp) => cp.Rings.Count > 1))
		{
			throw new Exception("Can't flag complex polygon triangles for deletion here. Call complex method.");
		}
		List<int> intList = new List<int>();
		for (int ordinal = 0; ordinal < _tin.TriangleCount; ordinal++)
		{
			if (IsSuperTriangle(ordinal))
			{
				MarkDeleted(ordinal);
				intList.Add(ordinal);
			}
		}
		if (_polygons.Count == 0)
		{
			return;
		}
		HashSet<EdgeDefinition> perimeterEdges = GetPerimeterEdges(out var _);
		foreach (int start in GetFloodfiller(_tin, perimeterEdges).Start(intList))
		{
			MarkDeleted(start);
		}
	}

	protected virtual void MarkBadTrianglesAsDeleted_ComplexRings()
	{
		List<int> intList = new List<int>();
		for (int i = 0; i < _tin.TriangleDefinition.Count; i++)
		{
			if (IsSuperTriangle(i))
			{
				intList.Add(i);
				MarkDeleted(i);
			}
		}
		if (_polygons.Count == 0)
		{
			return;
		}
		HashSet<EdgeDefinition> perimeterEdges = GetPerimeterEdges(out var onePerNegativeAreaRing);
		FloodFillHelper floodfiller = GetFloodfiller(_tin, perimeterEdges);
		foreach (int start in floodfiller.Start(intList))
		{
			MarkDeleted(start);
		}
		foreach (EdgeDefinition onePerNegativeAreaRing2 in onePerNegativeAreaRing)
		{
			foreach (int start2 in floodfiller.Start(new int[1] { onePerNegativeAreaRing2.ATriangleIndex }, emitSeeds: true))
			{
				MarkDeleted(start2);
			}
		}
	}

	protected static FloodFillHelper GetFloodfiller(Tin tin, HashSet<EdgeDefinition> perimEdges)
	{
		return new FloodFillHelper((int tIdx) =>
		{
			if (tIdx == -1)
			{
				return (Memory<int>)null;
			}
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[tIdx];
			return new int[3] { triangleDefinition.at0, triangleDefinition.at1, triangleDefinition.at2 };
		}, (int from, int to) =>
		{
			if (from == -1 || to == -1)
			{
				return false;
			}
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[from];
			EdgeDefinition edgeDefinition = tin.GetEdgeDefinition(from, triangleDefinition.GetEdge012(to));
			return !perimEdges.Contains(edgeDefinition);
		});
	}

	protected HashSet<EdgeDefinition> GetPerimeterEdges(out List<EdgeDefinition> onePerNegativeAreaRing, bool errorOnRevisitFace = false)
	{
		onePerNegativeAreaRing = new List<EdgeDefinition>();
		HashSet<EdgeDefinition> edgeDefinitions = new HashSet<EdgeDefinition>();
		foreach (ComplexPolygon polygon2 in _polygons)
		{
			foreach (Polygon ring in polygon2.Rings)
			{
				Polygon polygon = null;
				if (_normalizedOffset == Vector.Zero)
				{
					polygon = ring;
				}
				else
				{
					polygon = ring.Clone();
					polygon.Translate(_normalizedOffset);
				}
				List<EdgeDefinition> edgesExactlyFollowing = _tin.FindEdgesExactlyFollowing(polygon);
				if (edgesExactlyFollowing.IsNullOrEmpty())
				{
					throw new Exception("No perimeter edges were found.");
				}
				for (int i = 0; i < edgesExactlyFollowing.Count; i++)
				{
					EdgeDefinition edgeDefinition = edgesExactlyFollowing[i];
					if (!edgeDefinitions.Add(edgeDefinition) && errorOnRevisitFace)
					{
						throw new StructuredException(Message.GenericError("Perimeter edge added twice - this will fail. Error detected near {Coordinate}", Tin.GetEdgeSegment(edgeDefinition).Midpoint()));
					}
				}
				if (ring.Area < 0.0)
				{
					onePerNegativeAreaRing.Add(edgesExactlyFollowing[0]);
				}
			}
		}
		return edgeDefinitions;
	}
}
