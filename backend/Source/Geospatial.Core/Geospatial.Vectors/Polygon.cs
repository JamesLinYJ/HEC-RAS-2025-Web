using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.Clipper2Lib;
using Geospatial.DataStructures;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors.Helpers;
using Geospatial.Vectors.Polylines;
using Geospatial.Vectors.Rasterizers;
using Utility;
using Utility.Extensions;
using Utility.Memory;
using Utility.Systems;
using Utility.Unsafe;

namespace Geospatial.Vectors;

public class Polygon : Polyline, ISimpleRing, ICloneable, IPointInPolygonChecker
{
	public const double DefaultFuzzyTolerance = 1E-05;

	public const double BestLabelPointDefaultRatio = 0.01;

	public const double BestLabelPointDefaultAbsMin = 0.01;

	public const double BestLabelPointDefaultAbsMax = double.MaxValue;

	public const int BestLabelPointDefaultMaxSearchDepth = 20;

	private readonly Versioned<double> _cachedArea = new Versioned<double>();

	private Versioned<Tin> _cachedDevTin;

	public override int SegmentCount => _points.Count;

	public double AreaTolerance => Math.Sqrt(Math.Abs(Area)) / 1000000.0;

	public double Area => _cachedArea.Get(VersionXY, this, ComputeArea);

	public double Perimeter => ComputePerimeter();

	private Versioned<Tin> CachedDevTin
	{
		get
		{
			if (_cachedDevTin == null)
			{
				_cachedDevTin = new Versioned<Tin>();
			}
			return _cachedDevTin;
		}
	}

	public Tin DevTin => CachedDevTin.Get(VersionXY, ComputeTin);

	Memory<Point> ISimpleRing.Points => GetPointBufferUnsafe();

	Extent ISimpleRing.Extent => Extent;

	int ISimpleRing.SegmentCount => _points.Count;

	int ISimpleRing.Count => _points.Count;

	public static bool IsValidPolygon(Polygon pg)
	{
		if (pg != null)
		{
			return pg.Points.Count >= 3;
		}
		return false;
	}

	public bool IsValid()
	{
		return IsValidPolygon(this);
	}

	public static Polygon NullPolygon()
	{
		return new Polygon();
	}

	Segment ISimpleRing.GetSegment(int segIdx)
	{
		return new Segment(_points[segIdx], _points[(segIdx + 1) % Count]);
	}

	int ISimpleRing.NextPointIndex(int ptIdx)
	{
		return (ptIdx + 1) % Count;
	}

	int ISimpleRing.PreviousPointIndex(int ptIdx)
	{
		return (Count + ptIdx - 1) % Count;
	}

	public Polygon()
	{
	}

	public Polygon(int capacity)
		: base(capacity)
	{
	}

	public Polygon(Point[] points)
		: this(points.AsMemory())
	{
	}

	public Polygon(Memory<Point> points)
		: base(points)
	{
	}

	public Polygon(Memory<Point> points, Memory<double> z)
		: base(points, z)
	{
	}

	public Polygon(Memory<Point> points, Memory<double> z, Memory<double> m)
		: base(points, z, m)
	{
	}

	public Polygon(List<Point> points)
		: base(points)
	{
	}

	public Polygon(List<Point> points, List<double> z)
		: base(points, z)
	{
	}

	public Polygon(List<Point> points, List<double> z, List<double> m)
		: base(points, z, m)
	{
	}

	public Polygon(IEnumerable<Point> points)
		: base(points)
	{
	}

	public new static Polygon CreateWithBuffer(PointCollection points)
	{
		Polygon polygon = new Polygon(0);
		polygon.AssignBuffers(points);
		return polygon;
	}

	public static Polygon CreateWithBuffer(List<Point> points, List<double> z = null, List<double> m = null)
	{
		Polygon polygon = new Polygon(0);
		polygon.AssignBuffers(points, z, m);
		return polygon;
	}

	public new static Polygon CreateWithBuffer(Point[] points, double[] z = null, double[] m = null)
	{
		Polygon polygon = new Polygon(0);
		polygon.AssignBuffers(points, z, m);
		return polygon;
	}

	public static Polygon CreateRectangle(Point center, double width, double height, double rotation)
	{
		Polygon polygon = FromExtent(Extent.CenteredOn(center, width, height));
		if (rotation == 0.0)
		{
			return polygon;
		}
		RotateTransformer rotateTransformer = new RotateTransformer(center, rotation, RotationUnits.Degrees, Orientation.Clockwise);
		Memory<Point> pointBufferUnsafe = polygon.GetPointBufferUnsafe();
		rotateTransformer.Transform(pointBufferUnsafe, pointBufferUnsafe);
		return polygon;
	}

	public static Polygon CreateTriangle(Point center, double width, double height, double rotation)
	{
		Polygon polygon = CreateWithBuffer(new PointCollection
		{
			new Point(center.X, center.Y + height / 2.0),
			new Point(center.X - width / 2.0, center.Y - height / 2.0),
			new Point(center.X + width / 2.0, center.Y - height / 2.0)
		});
		if (rotation == 0.0)
		{
			return polygon;
		}
		RotateTransformer rotateTransformer = new RotateTransformer(center, rotation, RotationUnits.Degrees, Orientation.Clockwise);
		Memory<Point> pointBufferUnsafe = polygon.GetPointBufferUnsafe();
		rotateTransformer.Transform(pointBufferUnsafe, pointBufferUnsafe);
		return polygon;
	}

	public static Polygon CreateSquarePolygon(Point center, double length, double rotation)
	{
		return CreateRectangle(center, length, length, rotation);
	}

	public int PreviousPointIndex(int pointIndex)
	{
		return (PointCount + pointIndex - 1) % Count;
	}

	public int NextPointIndex(int pointIndex)
	{
		return (pointIndex + 1) % Count;
	}

	public override int PreviousSegmentIndex(int segIdx)
	{
		return (SegmentCount + segIdx - 1) % SegmentCount;
	}

	public override int NextSegmentIndex(int segIdx)
	{
		return (segIdx + 1) % SegmentCount;
	}

	public bool Overlaps(Polygon other)
	{
		throw new NotImplementedException();
	}

	private static double ComputeArea(Polygon pgon)
	{
		return ComputeArea(UnsafeHelpers.GetListRawBackingBuffer(pgon._points), pgon.PointCount, (Point[] pg, int i) => pg[i]);
	}

	private double ComputePerimeter()
	{
		return Length + _points.Last().DistanceTo(_points.First());
	}

	public static double ComputeArea(int nPoints, Func<int, Point> getPoint)
	{
		return ComputeArea(getPoint, nPoints, (Func<int, Point> func, int ii) => func(ii));
	}

	public static double ComputeArea<T>(T model, int nPoints, Func<T, int, Point> getPoint)
	{
		double computed = 0.0;
		for (int i = 0; i < nPoints; i++)
		{
			Point point = getPoint(model, i);
			Point point2 = getPoint(model, (i + 1) % nPoints);
			double point2PointTerm = point2.X - point.X;
			double y = point.Y;
			double y2 = point2.Y;
			computed += (y + y2) * point2PointTerm;
		}
		return computed * 0.5;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double ComputeArea_Op(in Segment segment)
	{
		double deltaX = segment.DeltaX;
		double y = segment.A.Y;
		double y2 = segment.B.Y;
		return (y + y2) * deltaX;
	}

	public double ComputeArea()
	{
		double area_OpSum = 0.0;
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this, isClosed: true);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			area_OpSum += ComputeArea_Op(in fastSegmentIterator[i]);
		}
		area_OpSum += ComputeArea_Op(fastSegmentIterator.GetClosingSegment());
		return area_OpSum * 0.5;
	}

	public bool IsClockwise()
	{
		return IsClockwise(Area);
	}

	public static bool IsClockwise(double area)
	{
		return area > 0.0;
	}

	public bool IsCounterClockwise()
	{
		return Area < 0.0;
	}

	public override Point ComputeCentroid()
	{
		if (!IsValid())
		{
			return Point.NullPoint;
		}
		return ComputeCentroid(GetPointBufferUnsafe());
	}

	public static Point ComputeCentroid(Memory<Point> pts)
	{
		Span<Point> ptsSpan = pts.Span;
		double computed = 0.0;
		double xComponent = 0.0;
		double yComponent = 0.0;
		Point point = ptsSpan[0];
		for (int i = 0; i < ptsSpan.Length - 1; i++)
		{
			Point point2 = ptsSpan[i];
			Point point3 = ptsSpan[i + 1];
			double pointPoint2Term = point.X + point2.X + point3.X;
			double pointPoint2Term2 = point.Y + point2.Y + point3.Y;
			double point2PointTerm = (point2.X - point.X) * (point3.Y - point.Y) - (point3.X - point.X) * (point2.Y - point.Y);
			xComponent += point2PointTerm * pointPoint2Term;
			yComponent += point2PointTerm * pointPoint2Term2;
			computed += point2PointTerm;
		}
		xComponent /= 3.0 * computed;
		yComponent /= 3.0 * computed;
		return new Point(xComponent, yComponent);
	}

	public new bool Contains(Point Point)
	{
		return Contains(Point.X, Point.Y);
	}

	public IPointInPolygonChecker GetBestPointInPolygonChecker()
	{
		if (Count < 512)
		{
			return this;
		}
		return new RingStripIndex(this);
	}

	public static int[] StableSort(IReadOnlyList<Polygon> polygons)
	{
		int count = polygons.Count;
		double[] absArea = new double[count];
		Extent[] extents = new Extent[count];
		int[] intValues = new int[count];
		for (int i = 0; i < count; i++)
		{
			absArea[i] = Math.Abs(polygons[i].Area);
			extents[i] = polygons[i].Extent;
			intValues[i] = i;
		}
		Array.Sort(intValues, (int a, int b) =>
		{
			int absAreaTerm = absArea[b].CompareTo(absArea[a]);
			if (absAreaTerm != 0)
			{
				return absAreaTerm;
			}
			int minXExtentsTerm = extents[a].MinX.CompareTo(extents[b].MinX);
			if (minXExtentsTerm != 0)
			{
				return minXExtentsTerm;
			}
			int minYExtentsTerm = extents[a].MinY.CompareTo(extents[b].MinY);
			return (minYExtentsTerm != 0) ? minYExtentsTerm : a.CompareTo(b);
		});
		return intValues;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Contains_SegmentOp(in Segment segment, double px, double py)
	{
		if (((segment.A.X <= px && segment.B.X > px) || (segment.B.X <= px && segment.A.X > px)) && py > segment.InterpolatedY(px))
		{
			return 1;
		}
		return 0;
	}

	public bool Contains(double px, double py)
	{
		Extent extent = Extent;
		if (px < extent.MinX || px > extent.MaxX || py < extent.MinY || py > extent.MaxY)
		{
			return false;
		}
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this, isClosed: true);
		int contains_SegmentOpSum = 0;
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			contains_SegmentOpSum += Contains_SegmentOp(in fastSegmentIterator[i], px, py);
		}
		contains_SegmentOpSum += Contains_SegmentOp(fastSegmentIterator.GetClosingSegment(), px, py);
		return contains_SegmentOpSum % 2 > 0;
	}

	public bool ContainsAny(IList<Point> pts, double tol = 0.0)
	{
		if (tol == 0.0)
		{
			for (int i = 0; i < pts.Count; i++)
			{
				if (Contains(pts[i]))
				{
					return true;
				}
			}
		}
		else
		{
			for (int j = 0; j < pts.Count; j++)
			{
				if (ContainsFuzzy(pts[j], tol))
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool ContainsAny(IReadOnlyList<Point> pts, double tol = 0.0)
	{
		if (tol == 0.0)
		{
			for (int i = 0; i < pts.Count; i++)
			{
				if (Contains(pts[i]))
				{
					return true;
				}
			}
		}
		else
		{
			for (int j = 0; j < pts.Count; j++)
			{
				if (ContainsFuzzy(pts[j], tol))
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool ContainsAll(IList<Point> pts)
	{
		for (int i = 0; i < pts.Count; i++)
		{
			if (!Contains(pts[i]))
			{
				return false;
			}
		}
		return true;
	}

	public bool ContainsAll(IReadOnlyList<Point> pts)
	{
		for (int i = 0; i < pts.Count; i++)
		{
			if (!Contains(pts[i]))
			{
				return false;
			}
		}
		return true;
	}

	public bool ContainsAll(IList<Point> pts, double tol)
	{
		for (int i = 0; i < pts.Count; i++)
		{
			Point point = pts[i];
			if (!ContainsFuzzy(point.X, point.Y, tol))
			{
				return false;
			}
		}
		return true;
	}

	public bool ContainsFuzzy(Point ptm, double tol = 1E-05)
	{
		return ContainsFuzzy(ptm.X, ptm.Y, tol);
	}

	public bool ContainsFuzzy(double px, double py, double tol = 1E-05)
	{
		double distSquared;
		return ContainsFuzzy(px, py, out distSquared, tol);
	}

	public bool ContainsFuzzy(double px, double py, out double distSquared, double tol = 1E-05)
	{
		if (!Extent.ContainsRoughly(px, py, tol))
		{
			distSquared = 10.0 * tol * tol;
			return false;
		}
		if (Contains(px, py))
		{
			distSquared = 0.0;
			return true;
		}
		double tolPolynomial = tol * tol;
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this, isClosed: true);
		RelativePosition relativePosition;
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			distSquared = fastSegmentIterator[i].DistanceToPointSquared(px, py, out relativePosition);
			if (distSquared < tolPolynomial)
			{
				return true;
			}
		}
		distSquared = fastSegmentIterator.GetClosingSegment().DistanceToPointSquared(px, py, out relativePosition);
		return distSquared < tolPolynomial;
	}

	public bool Contains(Polygon pgon)
	{
		return Contains((Polyline)pgon);
	}

	public bool Contains(Polyline pline)
	{
		if (!ContainsAll(pline.Points))
		{
			return false;
		}
		if (Intersects(pline))
		{
			return false;
		}
		return true;
	}

	public bool ContainsFuzzy(Polyline pline, double tol = 1E-05)
	{
		for (int i = 0; i < pline.PointCount; i++)
		{
			Point ptm = pline.Points[i];
			if (!ContainsFuzzy(ptm, tol))
			{
				return false;
			}
		}
		if (Intersects(pline))
		{
			return false;
		}
		return true;
	}

	public bool Contains(Extent ext)
	{
		return Contains(FromExtent(ext));
	}

	public bool AreaIntersects(Polygon pgon)
	{
		if (ContainsAny(pgon.Points))
		{
			return true;
		}
		if (Intersects(pgon))
		{
			return true;
		}
		if (pgon.ContainsAny(Points))
		{
			return true;
		}
		if (pgon.Intersects(this))
		{
			return true;
		}
		return false;
	}

	public bool AreaIntersects(Extent ext)
	{
		for (int i = 0; i < PointCount; i++)
		{
			if (ext.ContainsOrTouches(Points[i]))
			{
				return true;
			}
		}
		if (Contains(ext.UpperLeft) || Contains(ext.UpperRight) || Contains(ext.LowerLeft) || Contains(ext.LowerRight))
		{
			return true;
		}
		if (FromExtent(ext).Intersects(this))
		{
			return true;
		}
		return false;
	}

	public bool AreaIntersectsBuffered(Polygon pgon, double buffer)
	{
		if (ContainsAny(pgon.Points, buffer))
		{
			return true;
		}
		if (Intersects(pgon, buffer))
		{
			return true;
		}
		if (pgon.ContainsAny(Points, buffer))
		{
			return true;
		}
		return false;
	}

	public bool ContainsOrIntersects(Polyline pline)
	{
		if (!Extent.Overlaps(pline.Extent))
		{
			return false;
		}
		if (ContainsAny(pline.Points) || Intersects(pline))
		{
			return true;
		}
		return false;
	}

	public bool ContainsOrIntersects(Extent ext)
	{
		if (Contains(ext.UpperLeft) || Contains(ext.UpperRight) || Contains(ext.LowerLeft) || Contains(ext.LowerRight) || Intersects(ext.TopWall) || Intersects(ext.BottomWall) || Intersects(ext.LeftWall) || Intersects(ext.RightWall))
		{
			return true;
		}
		return false;
	}

	public bool SelfIntersects()
	{
		return SelfIntersectsBruteForce();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool SelfIntersectsBruteForce_OuterLoopOp(ref FastSegmentIterator fsi, in Segment curSeg, int segCount, int curIdx)
	{
		for (int i = curIdx + 2; i < segCount; i++)
		{
			if (curSeg.Intersects(fsi[i]))
			{
				return true;
			}
		}
		if (curIdx != 0 && curIdx != segCount - 1)
		{
			return curSeg.Intersects(fsi.GetClosingSegment());
		}
		return false;
	}

	public bool SelfIntersectsBruteForce()
	{
		FastSegmentIterator fsi = new FastSegmentIterator(this, isClosed: true);
		int countWhenOpen = fsi.CountWhenOpen;
		for (int i = 0; i < countWhenOpen; i++)
		{
			if (SelfIntersectsBruteForce_OuterLoopOp(ref fsi, in fsi[i], countWhenOpen, i))
			{
				return true;
			}
		}
		return false;
	}

	public int CountClipPolyline(Polyline pline)
	{
		if (!Polyline.IsValidPolyline(pline))
		{
			return 0;
		}
		int ordinal = 0;
		List<LineIntersection> allIntersections = GetAllIntersections(pline);
		if (allIntersections.IsNullOrEmpty())
		{
			if (Contains(pline.First()))
			{
				return ordinal + 1;
			}
			return 0;
		}
		allIntersections = (from ptint in allIntersections
			orderby ptint.BIntersection.SegmentIndex, ptint.BIntersection.Fraction
			select ptint).ToList();
		bool plineTerm = !Contains(pline.First());
		PointOnPolyline pointOnPolyline = pline.FirstPointOnPolyline();
		for (int intervalIndex = 0; intervalIndex <= allIntersections.Count; intervalIndex++)
		{
			PointOnPolyline pointOnPolyline2 = ((intervalIndex != allIntersections.Count) ? allIntersections[intervalIndex].BIntersection : pline.LastPointOnPolyline());
			if (plineTerm)
			{
				pointOnPolyline = pointOnPolyline2;
				plineTerm = false;
				continue;
			}
			int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
			if (pointOnPolyline.Fraction > 0.9999)
			{
				segmentIndexOffset++;
			}
			int segmentIndex = pointOnPolyline2.SegmentIndex;
			if (pointOnPolyline2.Fraction < 0.0001)
			{
				segmentIndex--;
			}
			ordinal++;
			plineTerm = true;
		}
		return ordinal;
	}

	public List<Polyline> ClipPolyline(Polyline pline)
	{
		if (!Polyline.IsValidPolyline(pline))
		{
			return null;
		}
		List<Polyline> polylines = new List<Polyline>();
		List<LineIntersection> allIntersections = GetAllIntersections(pline);
		if (allIntersections.IsNullOrEmpty())
		{
			if (Contains(pline.First()))
			{
				polylines.Add(pline.Clone());
				return polylines;
			}
			return null;
		}
		allIntersections = (from ptint in allIntersections
			orderby ptint.BIntersection.SegmentIndex, ptint.BIntersection.Fraction
			select ptint).ToList();
		bool plineTerm = !Contains(pline.First());
		PointOnPolyline pointOnPolyline = pline.FirstPointOnPolyline();
		for (int intervalIndex = 0; intervalIndex <= allIntersections.Count; intervalIndex++)
		{
			PointOnPolyline pointOnPolyline2 = ((intervalIndex != allIntersections.Count) ? allIntersections[intervalIndex].BIntersection : pline.LastPointOnPolyline());
			if (plineTerm)
			{
				pointOnPolyline = pointOnPolyline2;
				plineTerm = false;
				continue;
			}
			Polyline polyline = new Polyline();
			polyline.Add(pointOnPolyline.Point);
			int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
			if (pointOnPolyline.Fraction > 0.9999)
			{
				segmentIndexOffset++;
			}
			int segmentIndex = pointOnPolyline2.SegmentIndex;
			if (pointOnPolyline2.Fraction < 0.0001)
			{
				segmentIndex--;
			}
			for (int segmentIndexOffset2 = segmentIndexOffset; segmentIndexOffset2 <= segmentIndex; segmentIndexOffset2++)
			{
				polyline.Add(pline.Points[segmentIndexOffset2]);
			}
			polyline.Add(pointOnPolyline2.Point);
			polylines.Add(polyline);
			plineTerm = true;
		}
		return polylines;
	}

	public List<Tuple<PointOnPolyline, PointOnPolyline>> EntryExitPoints(Polyline pline)
	{
		List<PointOnPolyline> pointOnPolylines = (from LineIntersection in pline.GetAllIntersections(this)
			select LineIntersection.AIntersection).ToList();
		Point point = pline.First();
		Point point2 = pline.Last();
		if (Contains(point))
		{
			pointOnPolylines.Insert(0, pline.FirstPointOnPolyline());
		}
		if (Contains(point2))
		{
			pointOnPolylines.Add(pline.LastPointOnPolyline());
		}
		List<Tuple<PointOnPolyline, PointOnPolyline>> valueTupleList = new List<Tuple<PointOnPolyline, PointOnPolyline>>();
		for (int intervalIndex = 0; intervalIndex <= pointOnPolylines.Count - 2; intervalIndex += 2)
		{
			PointOnPolyline pointOnPolyline = pointOnPolylines[intervalIndex];
			PointOnPolyline pointOnPolyline2 = pointOnPolylines[intervalIndex];
			valueTupleList.Add(new Tuple<PointOnPolyline, PointOnPolyline>(pointOnPolyline, pointOnPolyline2));
		}
		return valueTupleList;
	}

	public List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline)
	{
		List<Tuple<double, double>> containedSubsegments = new List<Tuple<double, double>>();
		if (!Polyline.IsValidPolyline(polyline))
		{
			return containedSubsegments;
		}
		List<LineIntersection> allIntersections = GetAllIntersections(polyline);
		bool condition = true;
		Segment segment = polyline.Segments.First();
		Point point = segment.A;
		double tolerance = 1E-05;
		if (DistanceToPoint(point) < tolerance)
		{
			point = segment.PointAtDistance(tolerance);
			if (allIntersections.Count > 0)
			{
				LineIntersection lineIntersection = allIntersections.First();
				if (lineIntersection.BIntersection.SegmentIndex == 0 && lineIntersection.BIntersection.Fraction * segment.Length < tolerance)
				{
					allIntersections.RemoveAt(0);
				}
			}
		}
		if (Contains(point))
		{
			if (allIntersections.Count == 0)
			{
				containedSubsegments.Add(new Tuple<double, double>(0.0, polyline.Length));
				return containedSubsegments;
			}
			condition = true;
		}
		else
		{
			if (allIntersections.Count == 0)
			{
				return containedSubsegments;
			}
			condition = false;
		}
		double[] cumulativeSegmentLengths = polyline.CumulativeSegmentLengths();
		double cumulativeSegmentLengthSum = 0.0;
		for (int i = 0; i <= allIntersections.Count - 1; i++)
		{
			LineIntersection lineIntersection2 = allIntersections[i];
			int segmentIndex = lineIntersection2.BIntersection.SegmentIndex;
			double fraction = lineIntersection2.BIntersection.Fraction;
			double cumulativeSegmentLengthSum2 = polyline.Segments[segmentIndex].Length * fraction;
			if (segmentIndex != 0)
			{
				cumulativeSegmentLengthSum2 += cumulativeSegmentLengths[segmentIndex - 1];
			}
			if (condition)
			{
				if (cumulativeSegmentLengthSum != cumulativeSegmentLengthSum2)
				{
					containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum2));
				}
				condition = false;
			}
			else
			{
				cumulativeSegmentLengthSum = cumulativeSegmentLengthSum2;
				condition = true;
			}
		}
		if (condition && cumulativeSegmentLengthSum != polyline.Length)
		{
			containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, polyline.Length));
		}
		return containedSubsegments;
	}

	public bool RotationInvariantEquals(Polygon other, double tolerance)
	{
		Point point = Points.First();
		int unity = -1;
		for (int i = 0; i < other.Count; i++)
		{
			Point point2 = other[i];
			if (point == point2)
			{
				unity = i;
				break;
			}
			if (tolerance > 0.0 && point.DistanceTo(point2) <= tolerance)
			{
				unity = i;
				break;
			}
		}
		if (unity == -1)
		{
			return false;
		}
		throw new NotImplementedException();
	}

	public Orientation PolygonOrientation()
	{
		if (ComputeArea() > 0.0)
		{
			return Orientation.Clockwise;
		}
		return Orientation.CounterClockwise;
	}

	public bool PointCloseToPerimeter(Point point, double nearDistance)
	{
		for (int i = 0; i < SegmentCount; i++)
		{
			if (Segments[i].DistanceToPoint(point) < nearDistance)
			{
				return true;
			}
		}
		return false;
	}

	public bool PointCloseToPerimeter(Point point, double nearDistance, SpatialIndex<int> segmentSpatialIndex)
	{
		PixelRange pixelRange = segmentSpatialIndex.Raster.GetPixelRange(Extent.CenteredOn(point, nearDistance * 2.0));
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				List<int> bin = segmentSpatialIndex.Bin(i, j);
				if (bin == null)
				{
					continue;
				}
				foreach (int bin2 in bin)
				{
					if (Segments[bin2].DistanceToPoint(point) < nearDistance)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	public int GetTravelDirection(PointOnPolyline startPoP, PointOnPolyline midPoP, PointOnPolyline endPoP)
	{
		if (IsPointBetweenCCW(startPoP, midPoP, endPoP))
		{
			return 1;
		}
		return -1;
	}

	public int GetTravelDirectionShortestArc(PointOnPolyline startPoP, PointOnPolyline endPoP)
	{
		double distanceAlong = DistanceAlong(startPoP);
		double distanceAlong2 = DistanceAlong(endPoP);
		double length = Length;
		double lengthSum = distanceAlong2 - distanceAlong;
		if (lengthSum < 0.0)
		{
			lengthSum += length;
		}
		if (lengthSum <= 0.5 * length)
		{
			return 1;
		}
		return -1;
	}

	public bool IsPointBetweenCCW(PointOnPolyline startPoP, PointOnPolyline midPoP, PointOnPolyline endPoP)
	{
		if (startPoP.SegmentIndex == endPoP.SegmentIndex)
		{
			if (startPoP.Fraction == endPoP.Fraction)
			{
				if (midPoP.SegmentIndex == startPoP.SegmentIndex && midPoP.Fraction == startPoP.Fraction)
				{
					return true;
				}
				return false;
			}
			if (startPoP.Fraction < endPoP.Fraction)
			{
				if (midPoP.SegmentIndex == startPoP.SegmentIndex && midPoP.Fraction > startPoP.Fraction && midPoP.Fraction < endPoP.Fraction)
				{
					return true;
				}
				return false;
			}
			if (midPoP.SegmentIndex == startPoP.SegmentIndex && midPoP.Fraction > endPoP.Fraction && midPoP.Fraction < startPoP.Fraction)
			{
				return false;
			}
			return true;
		}
		int segmentIndex = startPoP.SegmentIndex;
		while (true)
		{
			if (segmentIndex == SegmentCount)
			{
				segmentIndex = 0;
			}
			if (segmentIndex == midPoP.SegmentIndex)
			{
				if (midPoP.SegmentIndex == startPoP.SegmentIndex)
				{
					if (midPoP.Fraction > startPoP.Fraction)
					{
						return true;
					}
					return false;
				}
				if (midPoP.SegmentIndex == endPoP.SegmentIndex)
				{
					if (midPoP.Fraction < endPoP.Fraction)
					{
						return true;
					}
					return false;
				}
				return true;
			}
			if (segmentIndex == endPoP.SegmentIndex)
			{
				break;
			}
			segmentIndex++;
		}
		return false;
	}

	public bool Rasterize(RasterDefinition raster, bool[] mask, PixelRangeOptions rangeOption = PixelRangeOptions.CenterPixelCovered)
	{
		bool condition = false;
		if (raster.Overlaps(Extent))
		{
			PolygonRasterizer polygonRasterizer = new PolygonRasterizer(raster);
			Action<int, bool[]> fillCellAction = (int n, bool[] payload) =>
			{
				payload[n] = true;
			};
			switch (rangeOption)
			{
			case PixelRangeOptions.CenterPixelCovered:
				condition |= polygonRasterizer.Rasterize(this, mask, fillCellAction);
				break;
			case PixelRangeOptions.PartialPixelCovered:
				condition |= polygonRasterizer.RasterizePartialPixel(this, mask, fillCellAction);
				break;
			case PixelRangeOptions.CompletePixelCovered:
				condition |= polygonRasterizer.RasterizeCompletePixel(this, mask, (int n, bool fill, bool[] payload) =>
				{
					payload[n] = fill;
				});
				break;
			}
		}
		return condition;
	}

	public bool RasterizeOnto<T>(RasterDefinition raster, T[] mask, T setValue, PixelRangeOptions rangeOption = PixelRangeOptions.CenterPixelCovered) where T : struct
	{
		bool cellsSet = false;
		if (raster.Overlaps(Extent))
		{
			PolygonRasterizer polygonRasterizer = new PolygonRasterizer(raster);
			Action<int> fillCellAction = (int n) =>
			{
				mask[n] = setValue;
				cellsSet = true;
			};
			switch (rangeOption)
			{
			case PixelRangeOptions.CenterPixelCovered:
				polygonRasterizer.Rasterize(this, fillCellAction);
				break;
			case PixelRangeOptions.PartialPixelCovered:
				polygonRasterizer.RasterizePartialPixel(this, fillCellAction);
				break;
			case PixelRangeOptions.CompletePixelCovered:
			{
				Action<int, bool> fillOrUnfillCellAction = (int n, bool fill) =>
				{
					if (fill)
					{
						mask[n] = setValue;
					}
					else
					{
						mask[n] = default;
					}
					cellsSet = true;
				};
				polygonRasterizer.RasterizeCompletePixel(this, fillOrUnfillCellAction);
				break;
			}
			}
		}
		return cellsSet;
	}

	public override void Filter(double tolerance)
	{
		base.Filter(tolerance);
	}

	public override void Filter(double tolerance, int first, int last)
	{
		base.Filter(tolerance, first, last);
	}

	public void EnsurePositiveArea()
	{
		if (Area < 0.0)
		{
			Reverse();
		}
	}

	public void EnsureNegativeArea()
	{
		if (Area > 0.0)
		{
			Reverse();
		}
	}

	public void EnsureClockwise()
	{
		EnsurePositiveArea();
	}

	public void EnsureCounterClockwise()
	{
		EnsureNegativeArea();
	}

	public new Polygon CloneFiltered(double tolerance)
	{
		Polygon polygon = Clone();
		polygon.Filter(tolerance);
		return polygon;
	}

	public override void FilterByRadialDistance(double eps)
	{
		if (!(eps > 0.0) || Count < 4)
		{
			return;
		}
		double epsPolynomial = eps * eps;
		Point point = _points[0];
		Point point2 = point;
		int unity = 1;
		for (int i = 1; i < Count; i++)
		{
			if (!(point2.DistanceSquaredTo(_points[i]) < epsPolynomial))
			{
				if (i != unity)
				{
					MovePoint(i, unity);
				}
				point2 = _points[unity];
				unity++;
			}
		}
		if (unity >= 4 && point2.DistanceSquaredTo(point) < epsPolynomial)
		{
			unity--;
		}
		int ordinal = Math.Max(3, unity);
		if (ordinal < Count)
		{
			RemoveRange(ordinal);
		}
	}

	public new Polygon CloneFilteredRadialDistance(double eps)
	{
		Polygon polygon = Clone();
		polygon.FilterByRadialDistance(eps);
		return polygon;
	}

	public override void FilterByCornerKeeper(double eps, double cornerEps)
	{
		if (Count < 4)
		{
			return;
		}
		double epsPolynomial = ((eps > 0.0) ? (eps * eps) : 0.0);
		Point point = _points[0];
		Point point2 = point;
		Point point3 = point;
		int unity = 1;
		int count = Count;
		for (int i = 1; i < count; i++)
		{
			Point other = _points[i];
			if (point3.DistanceSquaredTo(other) < epsPolynomial)
			{
				Point point4 = ((i + 1 < count) ? _points[i + 1] : point);
				double otherPoint3Term = other.X - point3.X;
				double otherPoint3Term2 = other.Y - point3.Y;
				double point4OtherTerm = point4.X - other.X;
				double point4OtherTerm2 = point4.Y - other.Y;
				if (Math.Abs(otherPoint3Term * point4OtherTerm2 - otherPoint3Term2 * point4OtherTerm) < cornerEps)
				{
					continue;
				}
			}
			if (i != unity)
			{
				MovePoint(i, unity);
			}
			point2 = point3;
			point3 = _points[unity];
			unity++;
		}
		if (unity >= 4 && point3.DistanceSquaredTo(point) < epsPolynomial)
		{
			double point3Point2Term = point3.X - point2.X;
			double point3Point2Term2 = point3.Y - point2.Y;
			double pointPoint3Term = point.X - point3.X;
			double pointPoint3Term2 = point.Y - point3.Y;
			if (Math.Abs(point3Point2Term * pointPoint3Term2 - point3Point2Term2 * pointPoint3Term) < cornerEps)
			{
				unity--;
			}
		}
		int ordinal = Math.Max(3, unity);
		if (ordinal < Count)
		{
			RemoveRange(ordinal);
		}
	}

	public new Polygon CloneFilteredCornerKeeper(double eps, double cornerEps)
	{
		Polygon polygon = Clone();
		polygon.FilterByCornerKeeper(eps, cornerEps);
		return polygon;
	}

	public override void FilterByGridQuantization(double cellSize)
	{
		base.FilterByGridQuantization(cellSize);
		if (Count >= 4)
		{
			Point point = _points[0];
			Point point2 = _points[Count - 1];
			if (point.X == point2.X && point.Y == point2.Y)
			{
				RemoveRange(Count - 1);
			}
		}
	}

	public new Polygon CloneFilteredGridQuantization(double cellSize)
	{
		Polygon polygon = Clone();
		polygon.FilterByGridQuantization(cellSize);
		return polygon;
	}

	public Polygon CloneFilteredIterative(int iterationCount, int targetPointCount)
	{
		if (PointCount <= targetPointCount)
		{
			return Clone();
		}
		List<Point> points = new List<Point>();
		CopyTo(points);
		double unity = 1.0;
		Polygon polygon = CloneFiltered(unity);
		if (polygon.PointCount == targetPointCount)
		{
			return polygon;
		}
		double computed;
		Polygon result;
		double computed2;
		if (polygon.PointCount < targetPointCount)
		{
			computed = unity;
			result = polygon;
			double computed3 = unity;
			int ordinal = 0;
			while (true)
			{
				computed3 /= 2.0;
				Polygon polygon2 = CloneFiltered(computed3);
				if (polygon2.PointCount == targetPointCount)
				{
					return polygon2;
				}
				if (polygon2.PointCount >= targetPointCount)
				{
					break;
				}
				computed = computed3;
				result = polygon2;
				ordinal++;
				if (ordinal == 100)
				{
					return new Polygon(points);
				}
			}
			computed2 = computed3;
		}
		else
		{
			computed2 = unity;
			double computed4 = unity;
			int ordinal2 = 0;
			Polygon polygon3;
			while (true)
			{
				computed4 *= 2.0;
				polygon3 = CloneFiltered(computed4);
				if (polygon3.PointCount == targetPointCount)
				{
					return polygon3;
				}
				if (polygon3.PointCount <= targetPointCount)
				{
					break;
				}
				computed2 = computed4;
				ordinal2++;
				if (ordinal2 == 100)
				{
					return new Polygon(points);
				}
			}
			computed = computed4;
			result = polygon3;
		}
		for (int i = 0; i < iterationCount; i++)
		{
			double computed5 = (computed2 + computed) / 2.0;
			Polygon polygon4 = CloneFiltered(computed5);
			if (polygon4.PointCount == targetPointCount)
			{
				return polygon4;
			}
			if (polygon4.PointCount > targetPointCount)
			{
				computed2 = computed5;
				continue;
			}
			computed = computed5;
			result = polygon4;
		}
		return result;
	}

	public Polygon FilterByArea(int maxPoints, bool usePseudoPQueue = true, int pQueueSize = 1000)
	{
		if (maxPoints < 3)
		{
			maxPoints = 3;
		}
		Polygon polygon = new Polygon(maxPoints);
		CopyTo(polygon);
		if (usePseudoPQueue)
		{
			PseudoPriorityQueue pseudoPriorityQueue = new PseudoPriorityQueue(null, pQueueSize);
			for (int i = 0; i < polygon.PointCount; i++)
			{
				double triangleArea = polygon.GetTriangleArea(i);
				pseudoPriorityQueue.TryAdd(i, triangleArea);
			}
			while (polygon.PointCount > maxPoints)
			{
				int takeMin = pseudoPriorityQueue.TakeMin();
				if (takeMin == -1)
				{
					pseudoPriorityQueue.Regenerate();
					for (int j = 0; j < polygon.PointCount; j++)
					{
						double triangleArea2 = polygon.GetTriangleArea(j);
						pseudoPriorityQueue.TryAdd(j, triangleArea2);
					}
					takeMin = pseudoPriorityQueue.TakeMin();
				}
				polygon.RemoveAt(takeMin);
				pseudoPriorityQueue.NotifyValueChanged(takeMin - 1, polygon.GetTriangleArea(takeMin - 1));
				pseudoPriorityQueue.NotifyValueChanged(takeMin, polygon.GetTriangleArea(takeMin));
			}
		}
		else
		{
			List<double> doubleList = new List<double>();
			for (int k = 0; k < PointCount; k++)
			{
				double triangleArea3 = polygon.GetTriangleArea(k);
				doubleList.Add(triangleArea3);
			}
			while (polygon.PointCount > maxPoints)
			{
				int intervalIndex = 0;
				for (int l = 1; l < polygon.PointCount; l++)
				{
					if (doubleList[l] < doubleList[intervalIndex])
					{
						intervalIndex = l;
					}
				}
				polygon.RemoveAt(intervalIndex);
				doubleList.RemoveAt(intervalIndex);
				if (doubleList[intervalIndex - 1] < double.MaxValue)
				{
					doubleList[intervalIndex - 1] = polygon.GetTriangleArea(intervalIndex - 1);
				}
				if (doubleList[intervalIndex] < double.MaxValue)
				{
					doubleList[intervalIndex] = polygon.GetTriangleArea(intervalIndex);
				}
			}
		}
		return polygon;
	}

	private double GetTriangleArea(int idx)
	{
		if (idx == 0 || idx == Count - 1)
		{
			return double.MaxValue;
		}
		return Math.Abs(Vector.CrossProduct(base[idx - 1], base[idx], base[idx + 1]) / 2.0);
	}

	public ClippedGeometryState ClipPolygon(Extent extent, out IMemoryOwner<Point> memOwner, out int length)
	{
		length = -1;
		memOwner = null;
		if (!Extent.Overlaps(in extent))
		{
			return ClippedGeometryState.FullyOutOfBounds;
		}
		if (extent.Contains(Extent))
		{
			return ClippedGeometryState.FullyInbounds;
		}
		memOwner = SharedMemoryPool<Point>.Rent(Count);
		Memory<Point> resultPts = memOwner.Memory;
		if (ClipPolygon(_points, extent, ref resultPts, out length))
		{
			return ClippedGeometryState.Clipped;
		}
		return ClippedGeometryState.FullyOutOfBounds;
	}

	public static bool ClipPolygon(IReadOnlyList<Point> points, Extent extent, ref Memory<Point> resultPts, out int length)
	{
		int nc = 0;
		if (resultPts.IsEmpty)
		{
			resultPts = new Point[points.Count];
		}
		if (points.Count < 3)
		{
			length = -1;
			return false;
		}
		for (int i = 0; i < points.Count; i++)
		{
			IReadOnlyList<Point> readOnlyList = points;
			double x = readOnlyList[((i - 1 < 0) ? (^1) : ((Index)(i - 1))).GetOffset(readOnlyList.Count)].X;
			readOnlyList = points;
			ClipPolygon_InnerLoop(extent, x, readOnlyList[((i - 1 < 0) ? (^1) : ((Index)(i - 1))).GetOffset(readOnlyList.Count)].Y, points[i].X, points[i].Y, ref nc, ref resultPts);
		}
		length = nc;
		return nc > 2;
	}

	private static void ClipPolygon_InnerLoop(Extent extent, double xI, double yI, double xNextI, double yNextI, ref int nc, ref Memory<Point> resultPts)
	{
		double xNextIAboveXI = xNextI - xI;
		double yNextIAboveYI = yNextI - yI;
		double minXExtentTerm;
		double maxXExtentTerm;
		if (xNextIAboveXI > 0.0 || (xNextIAboveXI == 0.0 && xI > extent.MaxX))
		{
			minXExtentTerm = extent.MinX;
			maxXExtentTerm = extent.MaxX;
		}
		else
		{
			minXExtentTerm = extent.MaxX;
			maxXExtentTerm = extent.MinX;
		}
		double minYExtentTerm;
		double maxYExtentTerm;
		if (yNextIAboveYI > 0.0 || (yNextIAboveYI == 0.0 && yI > extent.MaxY))
		{
			minYExtentTerm = extent.MinY;
			maxYExtentTerm = extent.MaxY;
		}
		else
		{
			minYExtentTerm = extent.MaxY;
			maxYExtentTerm = extent.MinY;
		}
		double minValue = ((xNextIAboveXI == 0.0) ? double.MinValue : ((minXExtentTerm - xI) / xNextIAboveXI));
		double minValue2 = ((yNextIAboveYI == 0.0) ? double.MinValue : ((minYExtentTerm - yI) / yNextIAboveYI));
		double minValue3;
		double minValue4;
		if (minValue < minValue2)
		{
			minValue3 = minValue;
			minValue4 = minValue2;
		}
		else
		{
			minValue3 = minValue2;
			minValue4 = minValue;
		}
		if (!(1.0 >= minValue3))
		{
			return;
		}
		if (0.0 < minValue3)
		{
			ClipPolygonAdd(minXExtentTerm, minYExtentTerm, ref nc, ref resultPts);
		}
		if (!(1.0 >= minValue4))
		{
			return;
		}
		double xITerm;
		if (xNextIAboveXI != 0.0)
		{
			xITerm = (maxXExtentTerm - xI) / xNextIAboveXI;
		}
		else
		{
			xITerm = ((!((extent.MinX <= xI) & (xI <= extent.MaxX))) ? double.MinValue : double.MaxValue);
		}
		double yITerm;
		if (yNextIAboveYI != 0.0)
		{
			yITerm = (maxYExtentTerm - yI) / yNextIAboveYI;
		}
		else
		{
			yITerm = ((!((extent.MinY <= yI) & (yI <= extent.MaxY))) ? double.MinValue : double.MaxValue);
		}
		double computed = ((!(xITerm < yITerm)) ? yITerm : xITerm);
		if (!(0.0 < minValue4) && !(0.0 < computed))
		{
			return;
		}
		if (minValue4 <= computed)
		{
			if (0.0 < minValue4)
			{
				if (minValue > minValue2)
				{
					ClipPolygonAdd(minXExtentTerm, yI + minValue * yNextIAboveYI, ref nc, ref resultPts);
				}
				else
				{
					ClipPolygonAdd(xI + minValue2 * xNextIAboveXI, minYExtentTerm, ref nc, ref resultPts);
				}
			}
			if (1.0 > computed)
			{
				if (xITerm < yITerm)
				{
					ClipPolygonAdd(maxXExtentTerm, yI + xITerm * yNextIAboveYI, ref nc, ref resultPts);
				}
				else
				{
					ClipPolygonAdd(xI + yITerm * xNextIAboveXI, maxYExtentTerm, ref nc, ref resultPts);
				}
			}
			else
			{
				ClipPolygonAdd(xNextI, yNextI, ref nc, ref resultPts);
			}
		}
		else if (minValue > minValue2)
		{
			ClipPolygonAdd(minXExtentTerm, maxYExtentTerm, ref nc, ref resultPts);
		}
		else
		{
			ClipPolygonAdd(maxXExtentTerm, minYExtentTerm, ref nc, ref resultPts);
		}
		static void ClipPolygonAdd(double x, double y, ref int reference, ref Memory<Point> pts)
		{
			if (reference >= pts.Length)
			{
				pts = new Point[pts.Length * 2];
			}
			pts.Span[reference] = new Point(x, y);
			reference++;
		}
	}

	public List<Polygon> Buffer(double distance, int precision = 3)
	{
		JoinType joinType = JoinType.Round;
		PathD pathD = new PathD(PointCount);
		for (int i = 0; i < PointCount; i++)
		{
			pathD.Add(Points[i]);
		}
		PathsD pathsD = Clipper.InflatePaths(new PathsD { pathD }, distance, joinType, EndType.Polygon, 2.0, precision);
		List<Polygon> polygons = new List<Polygon>(pathsD.Count);
		foreach (PathD pathsD2 in pathsD)
		{
			if (pathsD2.Count >= 3)
			{
				Polygon pathsD3 = new Polygon(pathsD2);
				polygons.Add(pathsD3);
			}
		}
		return polygons;
	}

	public override Polyline Subsection(PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		return PointsBetweenInclusive(startPoint, endPoint);
	}

	public Polyline PointsBetweenInclusive(PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		Polyline polyline = new Polyline();
		polyline.Add(startPoint.Point);
		polyline.AddRange(PointsBetweenExclusive(startPoint, endPoint).Points);
		if (endPoint.Fraction > 0.0)
		{
			polyline.Add(endPoint.Point);
		}
		return polyline;
	}

	public Polyline PointsBetweenExclusive(PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		Polyline polyline = new Polyline();
		if (startPoint.Equals(endPoint))
		{
			return polyline;
		}
		if (endPoint.Fraction == 0.0)
		{
			if (endPoint.SegmentIndex > 0)
			{
				endPoint.SegmentIndex--;
				endPoint.Fraction = 1.0;
			}
			else
			{
				endPoint.Fraction = 1.0;
				endPoint.SegmentIndex = SegmentCount - 1;
			}
		}
		if (startPoint.LessThan(endPoint))
		{
			for (int i = startPoint.SegmentIndex + 1; i <= endPoint.SegmentIndex; i++)
			{
				polyline.Add(Points[i]);
			}
		}
		else
		{
			for (int j = startPoint.SegmentIndex + 1; j < Count; j++)
			{
				polyline.Add(Points[j]);
			}
			for (int k = 0; k <= endPoint.SegmentIndex; k++)
			{
				polyline.Add(Points[k]);
			}
		}
		return polyline;
	}

	public static Polygon FromExtent(IExtent ie)
	{
		Extent extent = ie.Extent;
		return new Polygon(4)
		{
			{ extent.MinX, extent.MinY },
			{ extent.MinX, extent.MaxY },
			{ extent.MaxX, extent.MaxY },
			{ extent.MaxX, extent.MinY }
		};
	}

	public static Polygon FromCircle(double radius, int points, Point center)
	{
		Polygon polygon = new Polygon();
		double scaledPiOverPoints = Math.PI * 2.0 / (double)points;
		for (int i = 0; i < points; i++)
		{
			double computed = (double)i * scaledPiOverPoints;
			double radiusTerm = Math.Cos(computed) * radius;
			double radiusTerm2 = Math.Sin(computed) * radius;
			polygon.Add(new Point(radiusTerm + center.X, radiusTerm2 + center.Y));
		}
		return polygon;
	}

	public static bool TryBuildFromSegments(IList<Segment> segs, out Polygon pg)
	{
		if (segs.Count <= 2)
		{
			pg = null;
			return false;
		}
		Extent ext = Extent.Union(segs);
		SpatialIndex<int> spIdx = new SpatialIndex<int>(ext, SpatialIndex.ReasonableBinCount(segs.Count));
		for (int i = 0; i < segs.Count; i++)
		{
			spIdx.Add(segs[i], i);
		}
		List<Point> points = new List<Point>(segs.Count);
		int outS2 = 0;
		Segment segment = segs[outS2];
		points.Add(segment.A);
		points.Add(segment.B);
		for (int j = 0; j < segs.Count - 1; j++)
		{
			if (!FindA(segment.B, outS2, out var outS))
			{
				pg = null;
				return false;
			}
			outS2 = outS;
			segment = segs[outS2];
			points.Add(segment.B);
		}
		if (!segment.B.CloseToXY(points.First(), 1E-06))
		{
			pg = null;
			return false;
		}
		pg = CreateWithBuffer(points);
		return true;
		bool FindA(Point p, int notS, out int reference)
		{
			List<int> elements = spIdx.GetElements(p, 1E-06);
			for (int k = 0; k < elements.Count; k++)
			{
				int element = elements[k];
				if (element != notS && segs[element].A.CloseToXY(p, 1E-06))
				{
					reference = element;
					return true;
				}
			}
			reference = -1;
			return false;
		}
	}

	public Point BestLabelPoint(double precisionRatio = 0.01, double absoluteMinPrecision = 0.01, double absoluteMaxPrecision = double.MaxValue, int maxSearchDepth = 20, PolygonFilter filter = null)
	{
		if (Area == 0.0)
		{
			return Point.NullPoint;
		}
		if (Math.Abs(Area) < Math.Pow(absoluteMinPrecision, 2.0))
		{
			return ComputeCentroid();
		}
		double areaPrecisionRatioTerm = Math.Sqrt(Math.Abs(Area)) * precisionRatio;
		if (areaPrecisionRatioTerm < absoluteMinPrecision)
		{
			areaPrecisionRatioTerm = absoluteMinPrecision;
		}
		if (areaPrecisionRatioTerm > absoluteMaxPrecision)
		{
			areaPrecisionRatioTerm = absoluteMaxPrecision;
		}
		Extent extent = Extent;
		if (extent.Area <= 0.0)
		{
			return extent.Center;
		}
		Polygon polygon = (filter ?? PolygonFilters.Default)(this, areaPrecisionRatioTerm);
		return BestLabelPointSearch.Run(new Polygon[1] { polygon }, extent, areaPrecisionRatioTerm, maxSearchDepth);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double SignedPointToPolygonDistance_Op(in Segment seg, Point pt, ref bool isInside)
	{
		Point a = seg.A;
		Point b = seg.B;
		if (a.Y > pt.Y != b.Y > pt.Y && pt.X < (b.X - a.X) * (pt.Y - a.Y) / (b.Y - a.Y) + a.X)
		{
			isInside = !isInside;
		}
		return seg.DistanceToPoint(pt);
	}

	public double SignedPointToPolygonDistance(Point pt)
	{
		double maxValue = double.MaxValue;
		bool isInside = false;
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			maxValue = Math.Min(maxValue, SignedPointToPolygonDistance_Op(in fastSegmentIterator[i], pt, ref isInside));
		}
		maxValue = Math.Min(maxValue, SignedPointToPolygonDistance_Op(fastSegmentIterator.GetClosingSegment(), pt, ref isInside));
		if (isInside)
		{
			return maxValue;
		}
		return 0.0 - maxValue;
	}

	private Tin ComputeTin()
	{
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory(new TriangulationParameters
		{
			Tolerance = AreaTolerance
		});
		delaunayTinFactory.AddPerimeter(this);
		return delaunayTinFactory.ComputeTIN();
	}

	public new Polygon Clone()
	{
		if (HasZ && HasM)
		{
			return new Polygon(_points, _z, _m);
		}
		if (HasZ)
		{
			return new Polygon(_points, _z);
		}
		if (HasM)
		{
			return new Polygon(_points, null, _z);
		}
		return new Polygon(_points);
	}

	object ICloneable.Clone()
	{
		return Clone();
	}

	public Polygon CloneReversed()
	{
		if (HasZ && HasM)
		{
			return CreateWithBuffer(_points.CloneReversed(), _z.CloneReversed(), _m.CloneReversed());
		}
		if (HasZ)
		{
			return CreateWithBuffer(_points.CloneReversed(), _z.CloneReversed());
		}
		if (HasM)
		{
			return CreateWithBuffer(_points.CloneReversed(), null, _m.CloneReversed());
		}
		return CreateWithBuffer(_points.CloneReversed());
	}

	public void Stream(BinaryWriter bw)
	{
		double[] doubleValues = new double[Points.Count * 2 - 1 + 1];
		for (int i = 0; i <= Points.Count - 1; i++)
		{
			doubleValues[i * 2] = Points[i].X;
			doubleValues[i * 2 + 1] = Points[i].Y;
		}
		Pipe.WriteArray(bw, doubleValues);
	}

	public new static Polygon Stream(BinaryReader br)
	{
		double[] readArray = Pipe.ReadArray<double>(br);
		if (readArray.Count() == 0)
		{
			return null;
		}
		Polygon polygon = new Polygon((int)((double)readArray.Length / 2.0));
		for (int i = 0; i < readArray.Length / 2; i++)
		{
			polygon.Add(new Point(readArray[i * 2], readArray[i * 2 + 1]));
		}
		return polygon;
	}
}
