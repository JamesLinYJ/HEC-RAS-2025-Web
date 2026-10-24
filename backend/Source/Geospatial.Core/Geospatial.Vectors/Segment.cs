using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Utility.Systems;
using Utility.Units;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebuggerDisplay(),nq}")]
public struct Segment : ISegment<Point>, IExtent, IStreamableObject
{
	public static readonly Segment NullSegment = new Segment(Point.NullPoint, Point.NullPoint);

	public const double DefaultEpsilon = 1E-06;

	public Point A;

	public Point B;

	[ThreadStatic]
	private static Point[] _transformSrc;

	[ThreadStatic]
	private static Point[] _transformDst;

	Point ISegment<Point>.PointA
	{
		get
		{
			return A;
		}
		set
		{
			A = value;
		}
	}

	Point ISegment<Point>.PointB
	{
		get
		{
			return B;
		}
		set
		{
			B = value;
		}
	}

	public double MinX => Math.Min(A.X, B.X);

	public double MaxX => Math.Max(A.X, B.X);

	public double MaxY => Math.Max(A.Y, B.Y);

	public double MinY => Math.Min(A.Y, B.Y);

	public double DeltaX => B.X - A.X;

	public double DeltaY => B.Y - A.Y;

	public double Width => DeltaX;

	public double Height => DeltaY;

	public double Length => A.DistanceTo(B);

	public double LengthSquared => A.DistanceSquaredTo(B);

	public double Slope => DeltaY / DeltaX;

	public double Bearing
	{
		get
		{
			double piSum = Math.Atan2(DeltaY, DeltaX);
			if (piSum < 0.0)
			{
				piSum += Math.PI * 2.0;
			}
			return piSum;
		}
	}

	public Extent Extent => new Extent(MinX, MaxX, MinY, MaxY);

	public static bool IsNull(Segment seg)
	{
		return seg == NullSegment;
	}

	public bool IsNull()
	{
		return IsNull(this);
	}

	public static double ComputeSlope(Point p1, Point p2)
	{
		return (p2.Y - p1.Y) / (p2.X - p1.X);
	}

	public static double ComputeSlope(double x1, double y1, double x2, double y2)
	{
		return (y2 - y1) / (x2 - x1);
	}

	public double BearingAs(DirectionConvention convention, Orientation orientation)
	{
		throw new NotImplementedException();
	}

	public static bool BearingInOrder(Segment s1, Segment s2, Segment s3, Orientation orientation)
	{
		double bearing = s1.Bearing;
		double bearing2 = s2.Bearing;
		double bearing3 = s3.Bearing;
		double piSum = bearing2 - bearing;
		if (piSum < 0.0)
		{
			piSum += Math.PI * 2.0;
		}
		double piSum2 = bearing3 - bearing;
		if (piSum2 < 0.0)
		{
			piSum2 += Math.PI * 2.0;
		}
		if (piSum2 > piSum && orientation == Orientation.CounterClockwise)
		{
			return true;
		}
		if (piSum2 < piSum && orientation == Orientation.Clockwise)
		{
			return true;
		}
		return false;
	}

	public double InterpolateBearing(Segment other, Orientation orientation)
	{
		double bearing = Bearing;
		double bearing2 = other.Bearing;
		if (bearing == bearing2)
		{
			return bearing;
		}
		double bearingBearing2Term = (bearing + bearing2) / 2.0;
		if (orientation == Orientation.CounterClockwise)
		{
			if (bearing < bearing2)
			{
				return bearingBearing2Term;
			}
			return Rotate(bearingBearing2Term);
		}
		if (bearing < bearing2)
		{
			return Rotate(bearingBearing2Term);
		}
		return bearingBearing2Term;
		static double Rotate(double rads)
		{
			double piSum = rads - Math.PI;
			if (piSum < 0.0)
			{
				piSum += Math.PI * 2.0;
			}
			return piSum;
		}
	}

	public Segment(Point ptA, Point ptB)
	{
		A = ptA;
		B = ptB;
	}

	public Segment(IPoint ptA, IPoint ptB)
	{
		A = new Point(ptA.X, ptA.Y);
		B = new Point(ptB.X, ptB.Y);
	}

	public Segment(double x1, double y1, double x2, double y2)
	{
		A = new Point(x1, y1);
		B = new Point(x2, y2);
	}

	public Extent GetExtent()
	{
		return new Extent(MinX, MaxX, MinY, MaxY);
	}

	public Point HigherPoint()
	{
		if (A.Y >= B.Y)
		{
			return A;
		}
		return B;
	}

	public Point LowerPoint()
	{
		if (A.Y >= B.Y)
		{
			return B;
		}
		return A;
	}

	public Point RighterPoint()
	{
		if (A.X >= B.X)
		{
			return A;
		}
		return B;
	}

	public Point LefterPoint()
	{
		if (A.X >= B.X)
		{
			return B;
		}
		return A;
	}

	public Segment Portion(double t0, double t1)
	{
		return new Segment(PointAtFraction(t0), PointAtFraction(t1));
	}

	public Point PointAtFraction(double t)
	{
		return new Point(A.X + t * DeltaX, A.Y + t * DeltaY);
	}

	public Point PointAtDistance(double distance)
	{
		return PointAtFraction(distance / Length);
	}

	public Point Midpoint()
	{
		return PointAtFraction(0.5);
	}

	public static Point Midpoint(Point a, Point b)
	{
		return new Segment(a, b).Midpoint();
	}

	public Polyline Densify(double maxLength)
	{
		double length = Length;
		if (length > maxLength)
		{
			int count = (int)Math.Ceiling(length / maxLength);
			Polyline polyline = new Polyline(2 + count - 1);
			polyline.Add(A);
			for (int i = 1; i < count; i++)
			{
				double t = (double)i / (double)count;
				polyline.Add(PointAtFraction(t));
			}
			polyline.Add(B);
			return polyline;
		}
		return AsPolyline();
	}

	public Point[] GetInternalDensificationPoints(double maxLength)
	{
		int internalDensifyPointCount = InternalDensifyPointCount(maxLength);
		if (internalDensifyPointCount == 0)
		{
			return null;
		}
		Point[] points = new Point[internalDensifyPointCount];
		GetInternalDensificationPointsInternal(points.AsSpan());
		return points;
	}

	public int GetInternalDensificationPoints(double maxLength, Span<Point> outPoints)
	{
		int internalDensifyPointCount = InternalDensifyPointCount(maxLength);
		if (internalDensifyPointCount == 0)
		{
			return 0;
		}
		if (internalDensifyPointCount > outPoints.Length)
		{
			return internalDensifyPointCount;
		}
		GetInternalDensificationPointsInternal(outPoints.Slice(0, internalDensifyPointCount));
		return internalDensifyPointCount;
	}

	private void GetInternalDensificationPointsInternal(Span<Point> outPoints)
	{
		int length = outPoints.Length;
		for (int i = 0; i < length; i++)
		{
			double t = (double)(i + 1) / (double)(length + 1);
			outPoints[i] = PointAtFraction(t);
		}
	}

	public int InternalDensifyPointCount(double maxLength)
	{
		double length = Length;
		if (length <= maxLength)
		{
			return 0;
		}
		return (int)Math.Ceiling(length / maxLength) - 1;
	}

	public Polyline AsPolyline()
	{
		return Polyline.FromSegment(this);
	}

	public double InterpolatedY(double x)
	{
		double deltaX = DeltaX;
		if (deltaX != 0.0)
		{
			double deltaXTerm = (B.X - x) / deltaX;
			if (deltaXTerm < 0.0 || deltaXTerm > 1.0)
			{
				return double.NaN;
			}
			return A.Y * deltaXTerm + B.Y * (1.0 - deltaXTerm);
		}
		return double.NaN;
	}

	public double InterpolatedX(double y)
	{
		double deltaY = DeltaY;
		if (deltaY != 0.0)
		{
			double deltaYTerm = (B.Y - y) / deltaY;
			if (deltaYTerm < 0.0 || deltaYTerm > 1.0)
			{
				return double.NaN;
			}
			return A.X * deltaYTerm + B.X * (1.0 - deltaYTerm);
		}
		return double.NaN;
	}

	private static double Interpolate(double fracAlong, double aValue, double bValue)
	{
		if (fracAlong <= 0.0)
		{
			return aValue;
		}
		if (fracAlong >= 1.0)
		{
			return bValue;
		}
		return aValue + fracAlong * (bValue - aValue);
	}

	public double Interpolate(Point pt, double aValue, double bValue)
	{
		NearestPoint(pt, out var t);
		return Interpolate(t, aValue, bValue);
	}

	public void ExtendA(double newSegLength)
	{
		double length = Length;
		double deltaXOverLength = DeltaX / length;
		double deltaYOverLength = DeltaY / length;
		A = new Point(B.X - deltaXOverLength * newSegLength, B.Y - deltaYOverLength * newSegLength);
	}

	public void ExtendB(double newSegLength)
	{
		double length = Length;
		double deltaXOverLength = DeltaX / length;
		double deltaYOverLength = DeltaY / length;
		B = new Point(A.X + deltaXOverLength * newSegLength, A.Y + deltaYOverLength * newSegLength);
	}

	public void Extend(double extendDistance)
	{
		double scaledExtendDistance = extendDistance / 2.0;
		double length = Length;
		double deltaXOverLength = DeltaX / length;
		double deltaYOverLength = DeltaY / length;
		Point a = new Point(A.X - deltaXOverLength * scaledExtendDistance, A.Y - deltaYOverLength * scaledExtendDistance);
		Point b = new Point(B.X + deltaXOverLength * scaledExtendDistance, B.Y + deltaYOverLength * scaledExtendDistance);
		A = a;
		B = b;
	}

	public bool PointIsLeft(Point p)
	{
		return DeltaX * (p.Y - A.Y) - DeltaY * (p.X - A.X) > 0.0;
	}

	public bool PointIsRight(Point p)
	{
		return DeltaX * (p.Y - A.Y) - DeltaY * (p.X - A.X) < 0.0;
	}

	public List<Point> Split(int divs)
	{
		List<Point> points = new List<Point>(divs + 1);
		double divsTerm = (B.X - A.X) / (double)divs;
		double divsTerm2 = (B.Y - A.Y) / (double)divs;
		for (int i = 0; i < divs; i++)
		{
			Point point = new Point(A.X + divsTerm * (double)i, A.Y + divsTerm2 * (double)i);
			points.Add(point);
		}
		points.Add(B);
		return points;
	}

	public Point NearestPoint(Point pt, out double t)
	{
		double lengthSquared = LengthSquared;
		if (lengthSquared == 0.0)
		{
			t = 0.0;
			return A;
		}
		double ptTerm = pt.X - A.X;
		double ptTerm2 = pt.Y - A.Y;
		t = (DeltaX * ptTerm + DeltaY * ptTerm2) / lengthSquared;
		if (t <= 0.0)
		{
			t = 0.0;
			return A;
		}
		if (t >= 1.0)
		{
			t = 1.0;
			return B;
		}
		return PointAtFraction(t);
	}

	public Point NearestPoint(Point pt)
	{
		double t;
		return NearestPoint(pt, out t);
	}

	public bool CloseTo(Point point, double tolerance)
	{
		double distance;
		return CloseTo(point, tolerance, out distance);
	}

	public bool CloseTo(Point pt, double tolerance, out Point nearPoint, out double t, out double actualDistance)
	{
		if (!WithinMBR(pt, tolerance))
		{
			nearPoint = Point.NullPoint;
			t = double.NaN;
			actualDistance = double.NaN;
			return false;
		}
		double lengthSquared = LengthSquared;
		if (lengthSquared == 0.0)
		{
			t = 0.0;
			nearPoint = A;
		}
		else
		{
			double ptTerm = pt.X - A.X;
			double ptTerm2 = pt.Y - A.Y;
			t = (DeltaX * ptTerm + DeltaY * ptTerm2) / lengthSquared;
			if (t <= 0.0)
			{
				t = 0.0;
				nearPoint = A;
			}
			else if (t >= 1.0)
			{
				t = 1.0;
				nearPoint = B;
			}
			else
			{
				nearPoint = PointAtFraction(t);
			}
		}
		actualDistance = pt.DistanceTo(nearPoint);
		return actualDistance < tolerance;
	}

	public bool CloseTo(Point pt, double tolerance, out double distance)
	{
		if (!WithinMBR(pt, tolerance))
		{
			distance = double.NaN;
			return false;
		}
		distance = DistanceToPoint(pt);
		return distance < tolerance;
	}

	private bool WithinMBR(Point pt, double tolerance)
	{
		if (pt.X < MinX - tolerance)
		{
			return false;
		}
		if (pt.X > MaxX + tolerance)
		{
			return false;
		}
		if (pt.Y < MinY - tolerance)
		{
			return false;
		}
		if (pt.Y > MaxY + tolerance)
		{
			return false;
		}
		return true;
	}

	public double FractionAlong(Point Point)
	{
		double pointTerm = Point.X - A.X;
		double pointTerm2 = Point.Y - A.Y;
		double length = Length;
		return (DeltaX * pointTerm + DeltaY * pointTerm2) / (length * length);
	}

	public double DistanceToPoint(Point pt)
	{
		RelativePosition relativePosition;
		return DistanceToPoint(pt.X, pt.Y, out relativePosition);
	}

	public double DistanceToPoint(double x, double y)
	{
		RelativePosition relativePosition;
		return DistanceToPoint(x, y, out relativePosition);
	}

	public double DistanceToPoint(double px, double py, out RelativePosition relativePosition)
	{
		return Math.Sqrt(DistanceToPointSquared(px, py, out relativePosition));
	}

	public double DistanceToPointSquared(Point pt)
	{
		RelativePosition relativePosition;
		return DistanceToPointSquared(pt.X, pt.Y, out relativePosition);
	}

	public double DistanceToPointSquared(Point pt, out RelativePosition relativePosition)
	{
		return DistanceToPointSquared(pt.X, pt.Y, out relativePosition);
	}

	public double DistanceToPointSquared(double px, double py, out RelativePosition relativePosition)
	{
		double lengthSquared = LengthSquared;
		if (lengthSquared == 0.0)
		{
			relativePosition = RelativePosition.Undefined;
			return A.DistanceSquaredTo(px, py);
		}
		double pxTerm = px - A.X;
		double pyTerm = py - A.Y;
		double deltaXDeltaYTerm = DeltaX * pxTerm + DeltaY * pyTerm;
		double lengthSquaredTerm = deltaXDeltaYTerm / lengthSquared;
		if (lengthSquaredTerm <= 0.0)
		{
			relativePosition = RelativePosition.OnOrBeforeA;
			return pxTerm * pxTerm + pyTerm * pyTerm;
		}
		if (lengthSquaredTerm >= 1.0)
		{
			relativePosition = RelativePosition.OnOrAfterB;
			return B.DistanceSquaredTo(px, py);
		}
		relativePosition = RelativePosition.BetweenAandB;
		double distanceSquaredTo = A.DistanceSquaredTo(px, py);
		double computed = lengthSquaredTerm * deltaXDeltaYTerm;
		return Math.Max(distanceSquaredTo - computed, 0.0);
	}

	public double PerpendicularSignedDistance(Point p)
	{
		double length = Length;
		if (length == 0.0)
		{
			return A.DistanceTo(p.X, p.Y);
		}
		double computed = p.X - A.X;
		double computed2 = p.Y - A.Y;
		return ((0.0 - computed) * DeltaY + computed2 * DeltaX) / length;
	}

	public bool ClipToExtent(RasterDefinition raster)
	{
		double t;
		double t2;
		return ClipToExtent(raster.MinX, raster.MaxX, raster.MinY, raster.MaxY, out t, out t2);
	}

	public bool ClipToExtent(Extent ext)
	{
		double t;
		double t2;
		return ClipToExtent(ext.MinX, ext.MaxX, ext.MinY, ext.MaxY, out t, out t2);
	}

	public bool ClipToExtent(double minX, double maxX, double minY, double maxY)
	{
		double t;
		double t2;
		return ClipToExtent(minX, maxX, minY, maxY, out t, out t2);
	}

	public bool ClipToExtent(double minX, double maxX, double minY, double maxY, out double t0, out double t1)
	{
		t0 = 0.0;
		t1 = 1.0;
		if (!GetExtent().Overlaps(minX, maxX, minY, maxY))
		{
			return false;
		}
		double deltaX = DeltaX;
		if (Clip_Parametric(0.0 - deltaX, A.X - minX, ref t0, ref t1) && Clip_Parametric(deltaX, maxX - A.X, ref t0, ref t1))
		{
			double deltaY = DeltaY;
			if (Clip_Parametric(0.0 - deltaY, A.Y - minY, ref t0, ref t1) && Clip_Parametric(deltaY, maxY - A.Y, ref t0, ref t1))
			{
				if (t1 < 1.0)
				{
					B.X = A.X + t1 * deltaX;
					B.Y = A.Y + t1 * deltaY;
				}
				if (t0 > 0.0)
				{
					A.X += t0 * deltaX;
					A.Y += t0 * deltaY;
				}
				return true;
			}
		}
		return false;
	}

	private static bool Clip_Parametric(double p, double q, ref double t0, ref double t1)
	{
		if (p < 0.0)
		{
			double t02 = q / p;
			if (t02 > t1)
			{
				return false;
			}
			if (t02 > t0)
			{
				t0 = t02;
			}
		}
		else if (p > 0.0)
		{
			double t12 = q / p;
			if (t12 < t0)
			{
				return false;
			}
			if (t12 < t1)
			{
				t1 = t12;
			}
		}
		else if (q < 0.0)
		{
			return false;
		}
		return true;
	}

	public bool ClipToExtent_EdgePedantic(Extent ext)
	{
		double t;
		double t2;
		return ClipToExtent_EdgePedantic(ext.MinX, ext.MaxX, ext.MinY, ext.MaxY, out t, out t2);
	}

	public bool ClipToExtent_EdgePedantic(double minX, double maxX, double minY, double maxY, out double t0, out double t1)
	{
		t0 = 0.0;
		t1 = 1.0;
		if (!GetExtent().Overlaps_EdgePedantic(minX, maxX, minY, maxY))
		{
			return false;
		}
		double deltaX = DeltaX;
		if (Clip_Parametric(0.0 - deltaX, A.X - minX, ref t0, ref t1) && Clip_Parametric(deltaX, maxX - A.X, ref t0, ref t1))
		{
			double deltaY = DeltaY;
			if (Clip_Parametric(0.0 - deltaY, A.Y - minY, ref t0, ref t1) && Clip_Parametric(deltaY, maxY - A.Y, ref t0, ref t1))
			{
				if (t1 < 1.0)
				{
					B.X = A.X + t1 * deltaX;
					B.Y = A.Y + t1 * deltaY;
				}
				if (t0 > 0.0)
				{
					A.X += t0 * deltaX;
					A.Y += t0 * deltaY;
				}
				return true;
			}
		}
		return false;
	}

	public bool MBROverlaps(IExtent ext)
	{
		return MBROverlaps(ext.Extent);
	}

	public bool MBROverlaps(Extent ext)
	{
		if (MaxX < ext.MinX)
		{
			return false;
		}
		if (MinX > ext.MaxX)
		{
			return false;
		}
		if (MinY > ext.MaxY)
		{
			return false;
		}
		if (MaxY < ext.MinY)
		{
			return false;
		}
		return true;
	}

	public bool MBROverlaps(double mbrMinX, double mbrMaxX, double mbrMinY, double mbrMaxY)
	{
		if (MaxX < mbrMinX)
		{
			return false;
		}
		if (MinX > mbrMaxX)
		{
			return false;
		}
		if (MinY > mbrMaxY)
		{
			return false;
		}
		if (MaxY < mbrMinY)
		{
			return false;
		}
		return true;
	}

	public bool MBROverlaps(Segment seg)
	{
		if (MaxX < seg.MinX)
		{
			return false;
		}
		if (MinX > seg.MaxX)
		{
			return false;
		}
		if (MinY > seg.MaxY)
		{
			return false;
		}
		if (MaxY < seg.MinY)
		{
			return false;
		}
		return true;
	}

	public bool MBROverlaps(in Segment seg, double tolerance)
	{
		double x;
		double x2;
		if (A.X < B.X)
		{
			x = A.X;
			x2 = B.X;
		}
		else
		{
			x = B.X;
			x2 = A.X;
		}
		double y;
		double y2;
		if (A.Y < B.Y)
		{
			y = A.Y;
			y2 = B.Y;
		}
		else
		{
			y = B.Y;
			y2 = A.Y;
		}
		double x3;
		double x4;
		if (seg.A.X < seg.B.X)
		{
			x3 = seg.A.X;
			x4 = seg.B.X;
		}
		else
		{
			x3 = seg.B.X;
			x4 = seg.A.X;
		}
		double y3;
		double y4;
		if (seg.A.Y < seg.B.Y)
		{
			y3 = seg.A.Y;
			y4 = seg.B.Y;
		}
		else
		{
			y3 = seg.B.Y;
			y4 = seg.A.Y;
		}
		if (x2 + tolerance < x3)
		{
			return false;
		}
		if (x - tolerance > x4)
		{
			return false;
		}
		if (y - tolerance > y4)
		{
			return false;
		}
		if (y2 + tolerance < y3)
		{
			return false;
		}
		return true;
	}

	public bool Intersects(Segment seg2, double epsilon = 1E-06)
	{
		double x;
		double y;
		double t;
		double t2;
		return Intersects(in seg2, out x, out y, out t, out t2, epsilon);
	}

	public bool Intersects(Segment seg2, out double x, out double y, double epsilon = 1E-06)
	{
		double t;
		double t2;
		return Intersects(in seg2, out x, out y, out t, out t2, epsilon);
	}

	public bool Intersects(in Segment seg2, out double x, out double y, out double t1, out double t2, double epsilon = 1E-06)
	{
		if (!MBROverlaps(in seg2, epsilon))
		{
			x = double.NaN;
			y = double.NaN;
			t1 = double.NaN;
			t2 = double.NaN;
			return false;
		}
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		double deltaX2 = seg2.DeltaX;
		double deltaY2 = seg2.DeltaY;
		double deltaY2DeltaXTerm = deltaY2 * deltaX - deltaX2 * deltaY;
		if (Math.Abs(deltaY2DeltaXTerm) < epsilon)
		{
			x = double.NaN;
			y = double.NaN;
			t1 = double.NaN;
			t2 = double.NaN;
			return false;
		}
		double seg2Term = A.Y - seg2.A.Y;
		double seg2Term2 = A.X - seg2.A.X;
		t1 = (deltaX2 * seg2Term - deltaY2 * seg2Term2) / deltaY2DeltaXTerm;
		t2 = (deltaX * seg2Term - deltaY * seg2Term2) / deltaY2DeltaXTerm;
		x = A.X + t1 * deltaX;
		y = A.Y + t1 * deltaY;
		if (t1 >= 0.0 && t1 <= 1.0 && t2 >= 0.0 && t2 <= 1.0)
		{
			return true;
		}
		if (t1 > 0.0 - epsilon && t1 < 1.0 + epsilon && t2 > 0.0 - epsilon && t2 < 1.0 + epsilon)
		{
			if (t1 < 0.0)
			{
				t1 = 0.0;
			}
			if (t1 > 1.0)
			{
				t1 = 1.0;
			}
			if (t2 < 0.0)
			{
				t2 = 0.0;
			}
			if (t2 > 1.0)
			{
				t2 = 1.0;
			}
			return true;
		}
		return false;
	}

	public bool Intersects(IList<Segment> segs, double epsilon = 1E-06)
	{
		for (int i = 0; i < segs.Count; i++)
		{
			if (Intersects(segs[i], out var _, out var _, out var _, out var _, epsilon))
			{
				return true;
			}
		}
		return false;
	}

	public bool Intersects(Circle circle, ref double t0, ref Point P0, ref double t1, ref Point P1)
	{
		double length = Length;
		Point center = circle.Center;
		double centerTerm = center.X - A.X;
		double centerTerm2 = center.Y - A.Y;
		double deltaXDeltaYTerm = (DeltaX * centerTerm + DeltaY * centerTerm2) / length;
		Point other = PointAtFraction(deltaXDeltaYTerm / length);
		double distanceTo = center.DistanceTo(other);
		if (distanceTo >= circle.Radius)
		{
			return false;
		}
		double radiusCircleTerm = Math.Sqrt(circle.Radius * circle.Radius - distanceTo * distanceTo);
		double computed = deltaXDeltaYTerm - radiusCircleTerm;
		double computed2 = deltaXDeltaYTerm + radiusCircleTerm;
		if (computed >= length)
		{
			return false;
		}
		if (computed2 <= 0.0)
		{
			return false;
		}
		if (computed < 0.0)
		{
			t0 = 0.0;
			P0 = A;
		}
		else
		{
			t0 = computed / length;
			P0 = PointAtFraction(t0);
		}
		if (computed2 >= length)
		{
			t1 = 1.0;
			P1 = B;
		}
		else
		{
			t1 = computed2 / length;
			P1 = PointAtFraction(t1);
		}
		return true;
	}

	public bool IntersectPoint(Segment seg2, out double x, out double y, out double t1, out double t2, double Epsilon = 1E-06)
	{
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		double deltaX2 = seg2.DeltaX;
		double deltaY2 = seg2.DeltaY;
		double deltaY2DeltaXTerm = deltaY2 * deltaX - deltaX2 * deltaY;
		if (deltaY2DeltaXTerm != 0.0)
		{
			double seg2Term = A.Y - seg2.A.Y;
			double seg2Term2 = A.X - seg2.A.X;
			t1 = (deltaX2 * seg2Term - deltaY2 * seg2Term2) / deltaY2DeltaXTerm;
			t2 = (deltaX * seg2Term - deltaY * seg2Term2) / deltaY2DeltaXTerm;
			x = A.X + t1 * deltaX;
			y = A.Y + t1 * deltaY;
			if (t1 >= 0.0 && t1 <= 1.0 && ((t2 >= 0.0) & (t2 <= 1.0)))
			{
				return true;
			}
			if (t1 < 0.0 && Math.Abs(x - A.X) < Epsilon && Math.Abs(y - A.Y) < Epsilon)
			{
				t1 = 0.0;
			}
			else if (t1 > 1.0 && Math.Abs(x - B.X) < Epsilon && Math.Abs(y - B.Y) < Epsilon)
			{
				t1 = 1.0;
			}
			if (t2 < 0.0 && Math.Abs(x - seg2.A.X) < Epsilon && Math.Abs(y - seg2.A.Y) < Epsilon)
			{
				t2 = 0.0;
			}
			else if (t2 > 1.0 && Math.Abs(x - seg2.B.X) < Epsilon && Math.Abs(y - seg2.B.Y) < Epsilon)
			{
				t2 = 1.0;
			}
			if (t1 >= 0.0 && t1 <= 1.0)
			{
				return (t2 >= 0.0) & (t2 <= 1.0);
			}
			return false;
		}
		x = double.NaN;
		y = double.NaN;
		t1 = double.NaN;
		t2 = double.NaN;
		return false;
	}

	public bool IntersectPointCheckNodes(Segment seg2, out double x, out double y, out double t1, out double t2, double Epsilon = 1E-06)
	{
		if (seg2.DeltaY * DeltaX - seg2.DeltaX * DeltaY == 0.0)
		{
			x = (y = (t1 = (t2 = double.NaN)));
			return false;
		}
		if (A == seg2.A)
		{
			t1 = 0.0;
			t2 = 0.0;
			x = A.X;
			y = A.Y;
			return true;
		}
		if (A == seg2.B)
		{
			t1 = 0.0;
			t2 = 1.0;
			x = A.X;
			y = A.Y;
			return true;
		}
		if (B == seg2.A)
		{
			t1 = 1.0;
			t2 = 0.0;
			x = B.X;
			y = B.Y;
			return true;
		}
		if (B == seg2.B)
		{
			t1 = 1.0;
			t2 = 1.0;
			x = B.X;
			y = B.Y;
			return true;
		}
		return IntersectPoint(seg2, out x, out y, out t1, out t2, Epsilon);
	}

	public static bool IntersectsLineSegment(Point p1, Point p2, Point q1, Point q2)
	{
		double p2P1Term = p2.X - p1.X;
		double p2P1Term2 = p2.Y - p1.Y;
		double q1P1Term = q1.X - p1.X;
		double q1P1Term2 = q1.Y - p1.Y;
		double q2Q1Term = q2.X - q1.X;
		double q2Q1Term2 = q2.Y - q1.Y;
		double computed = q2Q1Term * p2P1Term2 - p2P1Term * q2Q1Term2;
		if (computed == 0.0)
		{
			return false;
		}
		double computed2 = (q2Q1Term * q1P1Term2 - q1P1Term * q2Q1Term2) / computed;
		double computed3 = (p2P1Term * q1P1Term2 - q1P1Term * p2P1Term2) / computed;
		if (computed2 >= 0.0 && computed2 <= 1.0 && computed3 >= 0.0)
		{
			return computed3 <= 1.0;
		}
		return false;
	}

	public static bool IntersectsLineSegment(Point p1, Point p2, Point q1, Point q2, out Point intersectingPoint)
	{
		intersectingPoint = Point.NullPoint;
		double p2P1Term = p2.X - p1.X;
		double p2P1Term2 = p2.Y - p1.Y;
		double q1P1Term = q1.X - p1.X;
		double q1P1Term2 = q1.Y - p1.Y;
		double q2Q1Term = q2.X - q1.X;
		double q2Q1Term2 = q2.Y - q1.Y;
		double computed = q2Q1Term * p2P1Term2 - p2P1Term * q2Q1Term2;
		if (computed == 0.0)
		{
			return false;
		}
		double computed2 = (q2Q1Term * q1P1Term2 - q1P1Term * q2Q1Term2) / computed;
		double computed3 = (p2P1Term * q1P1Term2 - q1P1Term * p2P1Term2) / computed;
		intersectingPoint = new Point(p1.X + (p2.X - p1.X) * computed2, p1.Y + (p2.Y - p1.Y) * computed2);
		if (computed2 >= 0.0 && computed2 <= 1.0 && computed3 >= 0.0)
		{
			return computed3 <= 1.0;
		}
		return false;
	}

	public bool Intersects(Extent extent)
	{
		if (MBROverlaps(extent))
		{
			Segment segment = this;
			return segment.ClipToExtent(extent);
		}
		return false;
	}

	public bool IsColinear(Point C, double tolerance, out bool sameDirection)
	{
		Segment segment = new Segment(A, C);
		Vector vector = segment.AsVector();
		sameDirection = vector.IsSameDirection(AsVector());
		double perpendicularSignedDistance = ((!(Length < vector.Length)) ? PerpendicularSignedDistance(C) : segment.PerpendicularSignedDistance(B));
		return Math.Abs(perpendicularSignedDistance) < tolerance;
	}

	public Segment Translate(Point delta)
	{
		return Translate(delta.X, delta.Y);
	}

	public Segment Translate(Vector delta)
	{
		return Translate(delta.X, delta.Y);
	}

	public Segment Translate(double dx, double dy)
	{
		return new Segment(A.Translate(dx, dy), B.Translate(dx, dy));
	}

	public Segment Transform(ISpatialTransformer transformer)
	{
		Point[] transformSrc = _transformSrc ?? (_transformSrc = new Point[2]);
		Point[] transformDst = _transformDst ?? (_transformDst = new Point[2]);
		transformSrc[0] = A;
		transformSrc[1] = B;
		transformer.Transform(transformSrc, transformDst);
		return new Segment(transformDst[0], transformDst[1]);
	}

	public Segment Scale(double tA, double tB)
	{
		return new Segment(PointAtFraction(tA), PointAtFraction(tB));
	}

	public Segment Reverse()
	{
		return new Segment(B, A);
	}

	public Segment Offset(double distance)
	{
		return Offset(distance, distance);
	}

	public Segment Offset(double Adistance, double Bdistance)
	{
		Vector vector = PerpendicularUnitVector();
		Vector vec = vector.Ratio(Adistance);
		Vector vec2 = vector.Ratio(Bdistance);
		return new Segment(A.Translate(vec), B.Translate(vec2));
	}

	public Segment StepForward()
	{
		Segment result = default;
		result.A = B;
		result.B.X = B.X + DeltaX;
		result.B.Y = B.Y + DeltaY;
		return result;
	}

	public Segment StepBack()
	{
		Segment result = default;
		result.B = A;
		result.A.X = A.X - DeltaX;
		result.A.Y = A.Y - DeltaY;
		return result;
	}

	public Vector AsVector()
	{
		return new Vector(DeltaX, DeltaY);
	}

	public Vector UnitVector()
	{
		return AsVector().UnitVector();
	}

	public Vector PerpendicularUnitVector()
	{
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		Vector result = default;
		if (deltaX == 0.0)
		{
			result.DeltaX = Math.Sign(0.0 - deltaY);
			result.DeltaY = 0.0;
		}
		else
		{
			if (deltaY != 0.0)
			{
				result.DeltaX = 0.0 - deltaY;
				result.DeltaY = deltaX;
				return result.UnitVector();
			}
			result.DeltaX = 0.0;
			result.DeltaY = Math.Sign(deltaX);
		}
		return result;
	}

	public Segment PerpendicularSegment()
	{
		double scaledDeltaX = DeltaX * 0.5;
		double scaledDeltaY = DeltaY * 0.5;
		Point point = Midpoint();
		return new Segment(new Point(point.X - scaledDeltaY, point.Y + scaledDeltaX), new Point(point.X + scaledDeltaY, point.Y - scaledDeltaX));
	}

	public Segment PerpendicularSegment(double newLength)
	{
		double scaledDeltaX = DeltaX * 0.5;
		double scaledDeltaY = DeltaY * 0.5;
		double newLengthOverLength = newLength / Length;
		scaledDeltaX *= newLengthOverLength;
		scaledDeltaY *= newLengthOverLength;
		Point point = Midpoint();
		return new Segment(new Point(point.X - scaledDeltaY, point.Y + scaledDeltaX), new Point(point.X + scaledDeltaY, point.Y - scaledDeltaX));
	}

	public void MoveMidpointTo(Point point)
	{
		Point point2 = Midpoint();
		Translate(point.X - point2.X, point.Y - point2.Y);
	}

	public Vector AveragedUnitVector(Segment segOther)
	{
		return new Vector(DeltaX + segOther.DeltaX, DeltaY + segOther.DeltaY).UnitVector();
	}

	public void Rasterize(RasterDefinition raster, Action<int> pixelAction)
	{
		Rasterize(raster, (Pixel px) =>
		{
			pixelAction(raster.CellIndex(px));
		});
	}

	public bool Rasterize<T>(RasterDefinition raster, T t, Action<int, T> pixelAction)
	{
		(Action<int, T>, T, RasterDefinition) t2 = (pixelAction, t, raster);
		return Rasterize(raster, t2, (Pixel px, (Action<int, T>, T, RasterDefinition) payload) =>
		{
			payload.Item1(payload.Item3.CellIndex(px), payload.Item2);
		});
	}

	public void Rasterize(RasterDefinition raster, Action<Pixel> pixelAction)
	{
		Segment segment = this;
		if ((!raster.Contains(A) || !raster.Contains(B)) && !ClipToExtent_EdgePedantic(raster.Extent))
		{
			this = segment;
			return;
		}
		int row = raster.Row(in A.Y);
		int col = raster.Col(in A.X);
		int row2 = raster.Row(in B.Y);
		int col2 = raster.Col(in B.X);
		if (row < 0)
		{
			row = 0;
		}
		else if (row == raster.Rows)
		{
			row = raster.Rows - 1;
		}
		if (col < 0)
		{
			col = 0;
		}
		else if (col == raster.Cols)
		{
			col = raster.Cols - 1;
		}
		if (row2 < 0)
		{
			row2 = 0;
		}
		else if (row2 == raster.Rows)
		{
			row2 = raster.Rows - 1;
		}
		if (col2 < 0)
		{
			col2 = 0;
		}
		else if (col2 == raster.Cols)
		{
			col2 = raster.Cols - 1;
		}
		int col2AboveCol = col2 - col;
		int row2AboveRow = row2 - row;
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		if (col2AboveCol == 0 && row2AboveRow == 0)
		{
			pixelAction(new Pixel(row, col));
		}
		else if (col2AboveCol == 0)
		{
			int ordinal = Math.Sign(row2AboveRow);
			for (int i = row; i != row2; i += ordinal)
			{
				pixelAction(new Pixel(i, col));
			}
			pixelAction(new Pixel(row2, col));
		}
		else if (row2AboveRow == 0)
		{
			int ordinal2 = Math.Sign(col2AboveCol);
			for (int j = col; j != col2; j += ordinal2)
			{
				pixelAction(new Pixel(row, j));
			}
			pixelAction(new Pixel(row, col2));
		}
		else
		{
			int col3 = col;
			int row3 = row;
			pixelAction(new Pixel(row3, col3));
			if (col2AboveCol > 0)
			{
				if (row2AboveRow > 0)
				{
					while (col3 != col2 || row3 != row2)
					{
						double cellMinYRasterTerm = (raster.CellMinY(row3) - A.Y) / deltaY;
						double cellMaxXRasterTerm = (raster.CellMaxX(col3) - A.X) / deltaX;
						if ((cellMinYRasterTerm < cellMaxXRasterTerm && row3 < row2) || col3 == col2)
						{
							row3++;
						}
						else
						{
							col3++;
						}
						pixelAction(new Pixel(row3, col3));
					}
				}
				else
				{
					while (col3 != col2 || row3 != row2)
					{
						double cellMaxYRasterTerm = (raster.CellMaxY(row3) - A.Y) / deltaY;
						double cellMaxXRasterTerm2 = (raster.CellMaxX(col3) - A.X) / deltaX;
						if ((cellMaxYRasterTerm < cellMaxXRasterTerm2 && row3 > row2) || col3 == col2)
						{
							row3--;
						}
						else
						{
							col3++;
						}
						pixelAction(new Pixel(row3, col3));
					}
				}
			}
			else if (row2AboveRow > 0)
			{
				while (col3 != col2 || row3 != row2)
				{
					double cellMinYRasterTerm2 = (raster.CellMinY(row3) - A.Y) / deltaY;
					double cellMinXRasterTerm = (raster.CellMinX(col3) - A.X) / deltaX;
					if ((cellMinYRasterTerm2 < cellMinXRasterTerm && row3 < row2) | (col3 == col2))
					{
						row3++;
					}
					else
					{
						col3--;
					}
					pixelAction(new Pixel(row3, col3));
				}
			}
			else
			{
				while (col3 != col2 || row3 != row2)
				{
					double cellMaxYRasterTerm2 = (raster.CellMaxY(row3) - A.Y) / deltaY;
					double cellMinXRasterTerm2 = (raster.CellMinX(col3) - A.X) / deltaX;
					if ((cellMaxYRasterTerm2 < cellMinXRasterTerm2 && row3 > row2) || col3 == col2)
					{
						row3--;
					}
					else
					{
						col3--;
					}
					pixelAction(new Pixel(row3, col3));
				}
			}
		}
		this = segment;
	}

	public bool Rasterize<T>(RasterDefinition raster, T t, Action<Pixel, T> pixelAction)
	{
		bool condition = false;
		Segment segment = this;
		if ((!raster.Contains(A) || !raster.Contains(B)) && !ClipToExtent_EdgePedantic(raster.Extent))
		{
			this = segment;
			return condition;
		}
		int row = raster.Row(in A.Y);
		int col = raster.Col(in A.X);
		int row2 = raster.Row(in B.Y);
		int col2 = raster.Col(in B.X);
		if (row < 0)
		{
			row = 0;
		}
		else if (row == raster.Rows)
		{
			row = raster.Rows - 1;
		}
		if (col < 0)
		{
			col = 0;
		}
		else if (col == raster.Cols)
		{
			col = raster.Cols - 1;
		}
		if (row2 < 0)
		{
			row2 = 0;
		}
		else if (row2 == raster.Rows)
		{
			row2 = raster.Rows - 1;
		}
		if (col2 < 0)
		{
			col2 = 0;
		}
		else if (col2 == raster.Cols)
		{
			col2 = raster.Cols - 1;
		}
		int col2AboveCol = col2 - col;
		int row2AboveRow = row2 - row;
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		if (col2AboveCol == 0 && row2AboveRow == 0)
		{
			condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
			pixelAction(new Pixel(row, col), t);
		}
		else if (col2AboveCol == 0)
		{
			int ordinal = Math.Sign(row2AboveRow);
			for (int i = row; i != row2; i += ordinal)
			{
				condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
				pixelAction(new Pixel(i, col), t);
			}
			condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
			pixelAction(new Pixel(row2, col), t);
		}
		else if (row2AboveRow == 0)
		{
			int ordinal2 = Math.Sign(col2AboveCol);
			for (int j = col; j != col2; j += ordinal2)
			{
				condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
				pixelAction(new Pixel(row, j), t);
			}
			condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
			pixelAction(new Pixel(row, col2), t);
		}
		else
		{
			int col3 = col;
			int row3 = row;
			condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
			pixelAction(new Pixel(row3, col3), t);
			if (col2AboveCol > 0)
			{
				if (row2AboveRow > 0)
				{
					while (col3 != col2 || row3 != row2)
					{
						double cellMinYRasterTerm = (raster.CellMinY(row3) - A.Y) / deltaY;
						double cellMaxXRasterTerm = (raster.CellMaxX(col3) - A.X) / deltaX;
						if ((cellMinYRasterTerm < cellMaxXRasterTerm && row3 < row2) || col3 == col2)
						{
							row3++;
						}
						else
						{
							col3++;
						}
						condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
						pixelAction(new Pixel(row3, col3), t);
					}
				}
				else
				{
					while (col3 != col2 || row3 != row2)
					{
						double cellMaxYRasterTerm = (raster.CellMaxY(row3) - A.Y) / deltaY;
						double cellMaxXRasterTerm2 = (raster.CellMaxX(col3) - A.X) / deltaX;
						if ((cellMaxYRasterTerm < cellMaxXRasterTerm2 && row3 > row2) || col3 == col2)
						{
							row3--;
						}
						else
						{
							col3++;
						}
						condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
						pixelAction(new Pixel(row3, col3), t);
					}
				}
			}
			else if (row2AboveRow > 0)
			{
				while (col3 != col2 || row3 != row2)
				{
					double cellMinYRasterTerm2 = (raster.CellMinY(row3) - A.Y) / deltaY;
					double cellMinXRasterTerm = (raster.CellMinX(col3) - A.X) / deltaX;
					if ((cellMinYRasterTerm2 < cellMinXRasterTerm && row3 < row2) | (col3 == col2))
					{
						row3++;
					}
					else
					{
						col3--;
					}
					condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
					pixelAction(new Pixel(row3, col3), t);
				}
			}
			else
			{
				while (col3 != col2 || row3 != row2)
				{
					double cellMaxYRasterTerm2 = (raster.CellMaxY(row3) - A.Y) / deltaY;
					double cellMinXRasterTerm2 = (raster.CellMinX(col3) - A.X) / deltaX;
					if ((cellMaxYRasterTerm2 < cellMinXRasterTerm2 && row3 > row2) || col3 == col2)
					{
						row3--;
					}
					else
					{
						col3--;
					}
					condition = (byte)((condition ? 1u : 0u) | 1u) != 0;
					pixelAction(new Pixel(row3, col3), t);
				}
			}
		}
		this = segment;
		return condition;
	}

	public double Integrate()
	{
		return Integrate(A, B);
	}

	public static double Integrate(Point p1, Point p2)
	{
		return Integrate(p1.X, p1.Y, p2.X, p2.Y);
	}

	public static double Integrate(double x1, double y1, double x2, double y2)
	{
		double x2AboveX1 = x2 - x1;
		if (y1 > 0.0 && y2 > 0.0)
		{
			double y1Y2Term = Math.Min(y1, y2);
			double absoluteY2AboveY1 = Math.Abs(y2 - y1);
			return y1Y2Term * x2AboveX1 + 0.5 * absoluteY2AboveY1 * x2AboveX1;
		}
		if (y1 < 0.0 && y2 < 0.0)
		{
			double y1Y2Term2 = Math.Max(y1, y2);
			double y2Y1Term = -1.0 * Math.Abs(y2 - y1);
			return y1Y2Term2 * x2AboveX1 + 0.5 * y2Y1Term * x2AboveX1;
		}
		if (y1 == 0.0)
		{
			return y2 * x2AboveX1 * 0.5;
		}
		if (y2 == 0.0)
		{
			return y1 * x2AboveX1 * 0.5;
		}
		double y2AboveY1OverX2AboveX1 = (y2 - y1) / x2AboveX1;
		double absoluteY1OverY2AboveY1OverX2AboveX1 = Math.Abs(y1 / y2AboveY1OverX2AboveX1);
		double y1Term = 0.5 * absoluteY1OverY2AboveY1OverX2AboveX1 * y1;
		double y2Term = 0.5 * (x2AboveX1 - absoluteY1OverY2AboveY1OverX2AboveX1) * y2;
		return y1Term + y2Term;
	}

	public Polygon GetOffsetPolygon(float width)
	{
		Segment segment = Offset(width);
		Segment segment2 = Offset(0f - width);
		return new Polygon(new Point[4] { segment.A, segment.B, segment2.B, segment2.A });
	}

	public Polygon GetOffsetPolygon(float distLeft, float distRight)
	{
		Segment segment = Offset(distLeft);
		Segment segment2 = Offset(0f - distRight);
		return new Polygon(new Point[4] { segment.A, segment.B, segment2.B, segment2.A });
	}

	public void GetOffsetPolygons(double distLeft, double distRight, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		Segment segment = Offset(distLeft);
		Segment segment2 = Offset(0.0 - distRight);
		leftPolygon = new Polygon(new Point[4] { segment.A, segment.B, B, A });
		rightPolygon = new Polygon(new Point[4] { A, B, segment2.B, segment2.A });
	}

	public void GetOffsetPolygonsExtended(float distLeft, float distRight, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		Segment segment = this;
		segment.Extend(distLeft);
		Segment segment2 = this;
		segment2.Extend(distRight);
		Segment segment3 = segment.Offset(distLeft);
		Segment segment4 = segment2.Offset(0f - distRight);
		leftPolygon = new Polygon(new Point[4] { segment3.A, segment3.B, segment.B, segment.A });
		rightPolygon = new Polygon(new Point[4] { segment2.A, segment2.B, segment4.B, segment4.A });
	}

	public void GetOffsetPolygonsExtendedTrapezoidal(float distLeft, float distRight, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		Segment segment = this;
		segment.Extend(distLeft);
		Segment segment2 = this;
		segment2.Extend(distRight);
		Segment segment3 = segment.Offset(distLeft);
		Segment segment4 = segment2.Offset(0f - distRight);
		leftPolygon = new Polygon(new Point[4] { segment3.A, segment3.B, B, A });
		rightPolygon = new Polygon(new Point[4] { A, B, segment4.B, segment4.A });
	}

	public Polyline ToPolyline()
	{
		return new Polyline(new Point[2] { A, B });
	}

	public static bool operator ==(Segment s1, Segment s2)
	{
		if (s1.A == s2.A)
		{
			return s1.B == s2.B;
		}
		return false;
	}

	public static bool operator !=(Segment s1, Segment s2)
	{
		return !(s1 == s2);
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (obj is Segment segment)
		{
			return segment == this;
		}
		return false;
	}

	public override string ToString()
	{
		return A.ToString() + " to " + B.ToString();
	}

	public override int GetHashCode()
	{
		return (17 * 29 + A.GetHashCode()) * 29 + B.GetHashCode();
	}

	private string DebuggerDisplay()
	{
		return $"{Math.Round(A.X, 5)}, {Math.Round(A.Y, 5)} -> {Math.Round(B.X, 5)}, {Math.Round(B.Y, 5)}";
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.Write(A.X);
		bw.Write(A.Y);
		bw.Write(B.X);
		bw.Write(B.Y);
	}

	public void StreamIn(BinaryReader br)
	{
		double x = br.ReadDouble();
		double y = br.ReadDouble();
		double x2 = br.ReadDouble();
		double y2 = br.ReadDouble();
		A = new Point(x, y);
		B = new Point(x2, y2);
	}
}
