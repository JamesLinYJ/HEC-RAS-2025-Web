using System;
using System.Runtime.InteropServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public struct SegmentM : IExtent
{
	public enum RelativePosition
	{
		Undefined,
		BeforeA,
		BetweenAandB,
		AfterB
	}

	public PointM A;

	public PointM B;

	public int Index;

	public double MaxX => System.Math.Max(A.X, B.X);

	public double MinX => System.Math.Min(A.X, B.X);

	public double MaxY => System.Math.Max(A.Y, B.Y);

	public double MinY => System.Math.Min(A.Y, B.Y);

	public double MaxZ => System.Math.Max(A.Z, B.Z);

	public double MinZ => System.Math.Min(A.Z, B.Z);

	public double MaxM => System.Math.Max(A.M, B.M);

	public double MinM => System.Math.Min(A.M, B.M);

	public bool MaxMinXYDefined
	{
		get
		{
			if (double.IsNaN(MaxX) || double.IsNaN(MinX) || double.IsNaN(MaxY) || double.IsNaN(MinY))
			{
				return false;
			}
			return true;
		}
	}

	public double Length => A.DistanceTo(B);

	public static SegmentM NullSegment()
	{
		return new SegmentM(PointM.NullPoint(), PointM.NullPoint());
	}

	public bool IsNull()
	{
		if (!A.IsNull())
		{
			return B.IsNull();
		}
		return true;
	}

	public SegmentM(PointM pA, PointM pB)
	{
		this = default;
		A = pA;
		B = pB;
	}

	public SegmentM(Point2D pA, Point2D pB)
	{
		this = default;
		A = new PointM(pA.X, pA.Y);
		B = new PointM(pB.X, pB.Y);
	}

	public SegmentM(PointM pA, PointM pB, int argIndex)
	{
		this = default;
		A = pA;
		B = pB;
		Index = argIndex;
	}

	public SegmentM(Point2Double pA, Point2Double pB)
	{
		this = default;
		A = new PointM(pA);
		B = new PointM(pB);
	}

	public SegmentM(double aX, double aY, double bX, double bY)
	{
		this = default;
		A.X = aX;
		A.Y = aY;
		B.X = bX;
		B.Y = bY;
	}

	public void Assign(PointM pA, PointM pB)
	{
		A = pA;
		B = pB;
	}

	public void Assign(Point2Double pA, Point2Double pB)
	{
		A = new PointM(pA);
		B = new PointM(pB);
	}

	public void Assign(PointM pA, PointM pB, int argIndex)
	{
		A = pA;
		B = pB;
		Index = argIndex;
	}

	public void SwapAB()
	{
		PointM a = A;
		A = B;
		B = a;
	}

	public bool EqualXY(SegmentM seg)
	{
		if (A.EqualXY(seg.A) && B.EqualXY(seg.B))
		{
			return true;
		}
		return false;
	}

	public bool EqualXYReversed(SegmentM seg)
	{
		if (A.EqualXY(seg.B) && B.EqualXY(seg.A))
		{
			return true;
		}
		return false;
	}

	public double DeltaX()
	{
		return B.X - A.X;
	}

	public double DeltaY()
	{
		return B.Y - A.Y;
	}

	public double DeltaZ()
	{
		return B.Z - A.Z;
	}

	public double DeltaM()
	{
		return B.M - A.M;
	}

	public bool MBROverlaps(IExtent ext)
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

	public bool MBROverlaps(SegmentM seg)
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

	public bool MBROverlaps(SegmentM seg, double tolerance)
	{
		if (MaxX + tolerance < seg.MinX)
		{
			return false;
		}
		if (MinX - tolerance > seg.MaxX)
		{
			return false;
		}
		if (MinY - tolerance > seg.MaxY)
		{
			return false;
		}
		if (MaxY + tolerance < seg.MinY)
		{
			return false;
		}
		return true;
	}

	public bool MBROverlaps(Feature feature)
	{
		if (MaxX < feature.MinX)
		{
			return false;
		}
		if (MinX > feature.MaxX)
		{
			return false;
		}
		if (MinY > feature.MaxY)
		{
			return false;
		}
		if (MaxY < feature.MinY)
		{
			return false;
		}
		return true;
	}

	public double Bearing()
	{
		double piSum = System.Math.Atan2(DeltaY(), DeltaX());
		if (piSum < 0.0)
		{
			piSum += System.Math.PI * 2.0;
		}
		return piSum;
	}

	public double Slope()
	{
		return DeltaY() / DeltaX();
	}

	public bool HasZ()
	{
		return A.HasZ() & B.HasZ();
	}

	public bool HasM()
	{
		return A.HasM() & B.HasM();
	}

	public PointM HigherPoint()
	{
		if (A.Y >= B.Y)
		{
			return A;
		}
		return B;
	}

	public PointM LowerPoint()
	{
		if (A.Y >= B.Y)
		{
			return B;
		}
		return A;
	}

	public PointM RighterPoint()
	{
		if (A.X >= B.X)
		{
			return A;
		}
		return B;
	}

	public PointM LefterPoint()
	{
		if (A.X >= B.X)
		{
			return B;
		}
		return A;
	}

	public SegmentM Portion(double t0, double t1)
	{
		SegmentM result = default;
		result.A = PointAtFraction(t0);
		result.B = PointAtFraction(t1);
		return result;
	}

	public PointM PointAtFraction(double t)
	{
		PointM result = default;
		result.X = A.X + t * DeltaX();
		result.Y = A.Y + t * DeltaY();
		if (HasZ())
		{
			result.Z = A.Z + t * DeltaZ();
		}
		else
		{
			result.Z = double.NaN;
		}
		if (HasM())
		{
			result.M = A.M + t * DeltaM();
		}
		else
		{
			result.M = double.NaN;
		}
		return result;
	}

	public PointM PointAtDistance(double distance)
	{
		double t = distance / Length;
		return PointAtFraction(t);
	}

	public PointM MidPoint()
	{
		PointM result = default;
		result.X = A.X + 0.5 * DeltaX();
		result.Y = A.Y + 0.5 * DeltaY();
		if (HasZ())
		{
			result.Z = A.Z + 0.5 * DeltaZ();
		}
		else
		{
			result.Z = double.NaN;
		}
		if (HasM())
		{
			result.M = A.M + 0.5 * DeltaM();
		}
		else
		{
			result.M = double.NaN;
		}
		return result;
	}

	public PointM MidPointXY()
	{
		PointM result = default;
		result.X = A.X + 0.5 * DeltaX();
		result.Y = A.Y + 0.5 * DeltaY();
		result.Z = double.NaN;
		result.M = double.NaN;
		return result;
	}

	public double ZAtFraction(double t)
	{
		return A.Z + DeltaZ() * t;
	}

	public double MAtFraction(double t)
	{
		return A.M + DeltaM() * t;
	}

	public PointMs AddDensifyPoints(double maxLength)
	{
		PointMs pointMs = new PointMs();
		double length = Length;
		checked
		{
			if (length > maxLength)
			{
				int systemLengthTerm = (int)System.Math.Ceiling(length / maxLength);
				int count = systemLengthTerm - 1;
				for (int i = 1; i <= count; i++)
				{
					double t = (double)i / (double)systemLengthTerm;
					pointMs.Add(PointAtFraction(t));
				}
			}
			return pointMs;
		}
	}

	public double Z(PointM pt, bool constrainToStartEndValues)
	{
		double fractionAlong = FractionAlong(pt);
		if (constrainToStartEndValues)
		{
			fractionAlong = RasMapperLib.Utilities.Math.GetInRange(fractionAlong, 0.0, 1.0);
		}
		return A.Z * (1.0 - fractionAlong) + B.Z * fractionAlong;
	}

	public double InterpolatedY(double x)
	{
		double computed = B.X - A.X;
		if (computed != 0.0)
		{
			double computed2 = (B.X - x) / computed;
			if (computed2 < 0.0 || computed2 > 1.0)
			{
				return double.NaN;
			}
			return A.Y * computed2 + B.Y * (1.0 - computed2);
		}
		return double.NaN;
	}

	public double InterpolatedX(double y)
	{
		double computed = B.Y - A.Y;
		if (computed != 0.0)
		{
			double computed2 = (B.Y - y) / computed;
			if (computed2 < 0.0 || computed2 > 1.0)
			{
				return double.NaN;
			}
			return A.X * computed2 + B.X * (1.0 - computed2);
		}
		return double.NaN;
	}

	public double FractionAlong(PointM PointM)
	{
		double pointMTerm = PointM.X - A.X;
		double pointMTerm2 = PointM.Y - A.Y;
		double length = Length;
		return (DeltaX() * pointMTerm + DeltaY() * pointMTerm2) / System.Math.Pow(length, 2.0);
	}

	public double DistanceToPoint(PointM pt, [Optional][DefaultParameterValue((RelativePosition)0)] ref RelativePosition relativePosition)
	{
		return DistanceToPoint(pt.X, pt.Y, ref relativePosition);
	}

	public double DistanceToPoint(double px, double py, [Optional][DefaultParameterValue((RelativePosition)0)] ref RelativePosition relativePosition)
	{
		double length = Length;
		if (length == 0.0)
		{
			relativePosition = RelativePosition.Undefined;
			return A.DistanceTo(px, py);
		}
		double pxTerm = px - A.X;
		double pyTerm = py - A.Y;
		double deltaXDeltaYTerm = (DeltaX() * pxTerm + DeltaY() * pyTerm) / length;
		if (deltaXDeltaYTerm < 0.0)
		{
			relativePosition = RelativePosition.BeforeA;
			return A.DistanceTo(px, py);
		}
		if (deltaXDeltaYTerm > length)
		{
			relativePosition = RelativePosition.AfterB;
			return B.DistanceTo(px, py);
		}
		double distanceTo = A.DistanceTo(px, py);
		double distanceToTerm = distanceTo * distanceTo - deltaXDeltaYTerm * deltaXDeltaYTerm;
		relativePosition = RelativePosition.BetweenAandB;
		if (distanceToTerm > 0.0)
		{
			return System.Math.Sqrt(distanceToTerm);
		}
		return 0.0;
	}

	public void ExtendA(double newSegLength)
	{
		double length = Length;
		double deltaXOverLength = DeltaX() / length;
		double deltaYOverLength = DeltaY() / length;
		A = new PointM(B.X - deltaXOverLength * newSegLength, B.Y - deltaYOverLength * newSegLength);
	}

	public void ExtendB(double newSegLength)
	{
		double length = Length;
		double deltaXOverLength = DeltaX() / length;
		double deltaYOverLength = DeltaY() / length;
		B = new PointM(A.X + deltaXOverLength * newSegLength, A.Y + deltaYOverLength * newSegLength);
	}

	public void Extend(double extendDistance)
	{
		double scaledExtendDistance = extendDistance / 2.0;
		double length = Length;
		double deltaXOverLength = DeltaX() / length;
		double deltaYOverLength = DeltaY() / length;
		PointM a = new PointM(A.X - deltaXOverLength * scaledExtendDistance, A.Y - deltaYOverLength * scaledExtendDistance);
		PointM b = new PointM(B.X + deltaXOverLength * scaledExtendDistance, B.Y + deltaYOverLength * scaledExtendDistance);
		A = a;
		B = b;
	}

	public double PerpendicularSignedDistance(PointM p)
	{
		double deltaX = DeltaX();
		double deltaY = DeltaY();
		double systemDeltaXTerm = System.Math.Pow(deltaX * deltaX + deltaY * deltaY, 0.5);
		if (systemDeltaXTerm == 0.0)
		{
			return A.DistanceTo(p.X, p.Y);
		}
		double computed = p.X - A.X;
		double computed2 = p.Y - A.Y;
		return ((0.0 - computed) * deltaY + computed2 * deltaX) / systemDeltaXTerm;
	}

	public PointM NearestPoint(PointM pt, [Optional][DefaultParameterValue(0.0)] ref double t)
	{
		double length = Length;
		if (length == 0.0)
		{
			t = 0.0;
			return A;
		}
		double ptTerm = pt.X - A.X;
		double ptTerm2 = pt.Y - A.Y;
		double deltaXDeltaYTerm = (DeltaX() * ptTerm + DeltaY() * ptTerm2) / length;
		if (deltaXDeltaYTerm < 0.0)
		{
			t = 0.0;
			return A;
		}
		if (deltaXDeltaYTerm > length)
		{
			t = 1.0;
			return B;
		}
		t = deltaXDeltaYTerm / Length;
		return PointAtFraction(t);
	}

	public bool CloseTo(PointM pt, double tolerance, [Optional][DefaultParameterValue(0.0)] ref double distance)
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
		PointM pt2 = pt;
		RelativePosition relativePosition = RelativePosition.Undefined;
		distance = DistanceToPoint(pt2, ref relativePosition);
		return distance < tolerance;
	}

	public bool PointIsLeft(PointM p)
	{
		return DeltaX() * (p.Y - A.Y) - DeltaY() * (p.X - A.X) > 0.0;
	}

	public bool PointIsLeft(Point2D p)
	{
		return DeltaX() * ((double)p.Y - A.Y) - DeltaY() * ((double)p.X - A.X) > 0.0;
	}

	public bool PointIsRight(PointM p)
	{
		return DeltaX() * (p.Y - A.Y) - DeltaY() * (p.X - A.X) < 0.0;
	}

	public bool PointIsRight(Point2D p)
	{
		return DeltaX() * ((double)p.Y - A.Y) - DeltaY() * ((double)p.X - A.X) < 0.0;
	}

	public bool Intersects(SegmentM seg2, double Epsilon = 0.01)
	{
		double x = default;
		double y = default;
		double t = default;
		double t2 = default;
		return Intersects(seg2, ref x, ref y, ref t, ref t2);
	}

	public bool Intersects(SegmentM seg2, ref double x, ref double y, double Epsilon = 0.01)
	{
		double t = default;
		double t2 = default;
		return Intersects(seg2, ref x, ref y, ref t, ref t2);
	}

	public bool Intersects(SegmentM seg2, ref double x, ref double y, ref double t1, ref double t2, double Epsilon = 1E-06)
	{
		if (MBROverlaps(seg2, Epsilon))
		{
			double deltaX = DeltaX();
			double deltaY = DeltaY();
			double deltaX2 = seg2.DeltaX();
			double deltaY2 = seg2.DeltaY();
			double deltaY2DeltaXTerm = deltaY2 * deltaX - deltaX2 * deltaY;
			if (deltaY2DeltaXTerm != 0.0)
			{
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
				if (t1 > 0.0 - Epsilon && t1 < 1.0 + Epsilon && t2 > 0.0 - Epsilon && t2 < 1.0 + Epsilon)
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
			}
		}
		return false;
	}

	public bool Intersects(ISegments poly, double epsilon = 1E-06)
	{
		checked
		{
			int count = poly.SegmentCount() - 1;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				if (Intersects(poly.Segment(i), ref x, ref y, ref t, ref t2, epsilon))
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool Intersects(CircleM circle, ref double t0, ref PointM P0, ref double t1, ref PointM P1)
	{
		double length = Length;
		PointM pointM = circle.Center();
		double pointMTerm = pointM.X - A.X;
		double pointMTerm2 = pointM.Y - A.Y;
		double deltaXDeltaYTerm = (DeltaX() * pointMTerm + DeltaY() * pointMTerm2) / length;
		PointM ptM = PointAtFraction(deltaXDeltaYTerm / length);
		double distanceTo = pointM.DistanceTo(ptM);
		if (distanceTo >= circle.R)
		{
			return false;
		}
		double systemCircleTerm = System.Math.Sqrt(System.Math.Pow(circle.R, 2.0) - System.Math.Pow(distanceTo, 2.0));
		double computed = deltaXDeltaYTerm - systemCircleTerm;
		double computed2 = deltaXDeltaYTerm + systemCircleTerm;
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

	public bool IntersectPoint(SegmentM seg2, ref double x, ref double y, ref double t1, ref double t2, double Epsilon = 0.01)
	{
		double deltaX = DeltaX();
		double deltaY = DeltaY();
		double deltaX2 = seg2.DeltaX();
		double deltaY2 = seg2.DeltaY();
		double deltaY2DeltaXTerm = deltaY2 * deltaX - deltaX2 * deltaY;
		if (deltaY2DeltaXTerm != 0.0)
		{
			double seg2Term = A.Y - seg2.A.Y;
			double seg2Term2 = A.X - seg2.A.X;
			t1 = (deltaX2 * seg2Term - deltaY2 * seg2Term2) / deltaY2DeltaXTerm;
			t2 = (deltaX * seg2Term - deltaY * seg2Term2) / deltaY2DeltaXTerm;
			x = A.X + t1 * deltaX;
			y = A.Y + t1 * deltaY;
			if ((t1 >= 0.0 && t1 <= 1.0 && t2 >= 0.0) & (t2 <= 1.0))
			{
				return true;
			}
			if (System.Math.Abs(x - A.X) < Epsilon && System.Math.Abs(y - A.Y) < Epsilon)
			{
				t1 = 0.0;
				return true;
			}
			if (System.Math.Abs(x - B.X) < Epsilon && System.Math.Abs(y - B.Y) < Epsilon)
			{
				t1 = 1.0;
				return true;
			}
			if (System.Math.Abs(x - seg2.A.X) < Epsilon && System.Math.Abs(y - seg2.A.Y) < Epsilon)
			{
				t2 = 0.0;
				return true;
			}
			if (System.Math.Abs(x - seg2.B.X) < Epsilon && System.Math.Abs(y - seg2.B.Y) < Epsilon)
			{
				t2 = 1.0;
				return true;
			}
		}
		return false;
	}

	public bool Intersects(PointM PointM, double nearDistance)
	{
		RelativePosition relativePosition = RelativePosition.Undefined;
		if (DistanceToPoint(PointM, ref relativePosition) <= nearDistance)
		{
			return true;
		}
		return false;
	}

	public bool Intersects(Extent extent)
	{
		if (MBROverlaps(extent))
		{
			SegmentM segmentM = this;
			return segmentM.ClipToExtent(extent);
		}
		return false;
	}

	public SegmentM Translate(PointM delta)
	{
		SegmentM result = default;
		result.A = A.Translate(delta);
		result.B = B.Translate(delta);
		return result;
	}

	public SegmentM Translate(double dx, double dy)
	{
		SegmentM result = default;
		result.A = A.Translate(new PointM(dx, dy));
		result.B = B.Translate(new PointM(dx, dy));
		return result;
	}

	public void Transform(Transform transformer)
	{
		transformer.TransformPointM(ref A);
		transformer.TransformPointM(ref B);
	}

	public void Scale(double tA, double tB)
	{
		PointM a = PointAtFraction(tA);
		PointM b = PointAtFraction(tB);
		A = a;
		B = b;
	}

	public PointMs CreateArrow()
	{
		PointMs pointMs = new PointMs(7);
		pointMs.Add(A);
		pointMs.Add(B);
		new SegmentM(PointAtFraction(0.8), B).AddArrowHeadPoints(pointMs);
		return pointMs;
	}

	private void AddArrowHeadPoints(PointMs pts)
	{
		double deltaXTerm = DeltaX() / 3.0;
		double deltaYTerm = DeltaY() / 3.0;
		pts.Add(A);
		pts.Add(new PointM(A.X - deltaXTerm - deltaYTerm, A.Y - deltaYTerm + deltaXTerm));
		pts.Add(B);
		pts.Add(new PointM(A.X - deltaXTerm + deltaYTerm, A.Y - deltaYTerm - deltaXTerm));
		pts.Add(A);
	}

	public PointMs CreateArcArrow()
	{
		double length = Length;
		double lengthSystemTerm = length * System.Math.Sqrt(3.0) * 0.5;
		PointM pointM = PerpendicularUnitVector();
		PointM pA = MidPoint().Translate(pointM.Ratio(0.0 - lengthSystemTerm));
		PointMs pointMs = new PointMs(13);
		double bearing = new SegmentM(pA, A).Bearing();
		double scaledPi = System.Math.PI / 36.0;
		int ordinal = 0;
		do
		{
			pointMs.Add(pA.X + length * System.Math.Cos(bearing), pA.Y + length * System.Math.Sin(bearing));
			bearing -= scaledPi;
			ordinal = checked(ordinal + 1);
		}
		while (ordinal <= 12);
		new SegmentM(pointMs[11], pointMs[12]).AddArrowHeadPoints(pointMs);
		return pointMs;
	}

	public SegmentM Reverse()
	{
		SegmentM result = default;
		result.A = B;
		result.B = A;
		return result;
	}

	public bool ClipToExtent(Extent extent)
	{
		double minX = extent.MinX;
		double maxX = extent.MaxX;
		double minY = extent.MinY;
		double maxY = extent.MaxY;
		double t = 0.0;
		double t2 = 1.0;
		return ClipToExtent(minX, maxX, minY, maxY, ref t, ref t2);
	}

	public bool ClipToExtent(IExtent extent)
	{
		double minX = extent.MinX;
		double maxX = extent.MaxX;
		double minY = extent.MinY;
		double maxY = extent.MaxY;
		double t = 0.0;
		double t2 = 1.0;
		return ClipToExtent(minX, maxX, minY, maxY, ref t, ref t2);
	}

	public bool ClipToExtent(RasterM raster)
	{
		double minX = raster.MinX;
		double maxX = raster.MaxX;
		double minY = raster.MinY;
		double maxY = raster.MaxY;
		double t = 0.0;
		double t2 = 1.0;
		return ClipToExtent(minX, maxX, minY, maxY, ref t, ref t2);
	}

	public bool ClipToExtent(RasterM raster, ref double t0, ref double t1)
	{
		return ClipToExtent(raster.MinX, raster.MaxX, raster.MinY, raster.MaxY, ref t0, ref t1);
	}

	public bool ClipToExtent(double extentMinX, double extentMaxX, double extentMinY, double extentMaxY, [Optional][DefaultParameterValue(0.0)] ref double t0, [Optional][DefaultParameterValue(1.0)] ref double t1)
	{
		if (!MBROverlaps(extentMinX, extentMaxX, extentMinY, extentMaxY))
		{
			return false;
		}
		t0 = 0.0;
		t1 = 1.0;
		double deltaX = DeltaX();
		if (Clip_Parametric(0.0 - deltaX, A.X - extentMinX, ref t0, ref t1) && Clip_Parametric(deltaX, extentMaxX - A.X, ref t0, ref t1))
		{
			double deltaY = DeltaY();
			if (Clip_Parametric(0.0 - deltaY, A.Y - extentMinY, ref t0, ref t1) && Clip_Parametric(deltaY, extentMaxY - A.Y, ref t0, ref t1))
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

	private bool Clip_Parametric(double p, double q, ref double t0, ref double t1)
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

	public SegmentM Offset(double distance)
	{
		return Offset(distance, distance);
	}

	public SegmentM Offset(double Adistance, double Bdistance)
	{
		PointM pointM = PerpendicularUnitVector();
		PointM delta = pointM.Ratio(Adistance);
		PointM delta2 = pointM.Ratio(Bdistance);
		return new SegmentM(A.Translate(delta), B.Translate(delta2));
	}

	public SegmentM StepForward()
	{
		SegmentM result = default;
		result.A = B;
		result.B.X = B.X + DeltaX();
		result.B.Y = B.Y + DeltaY();
		return result;
	}

	public SegmentM StepBack()
	{
		SegmentM result = default;
		result.B = A;
		result.A.X = A.X - DeltaX();
		result.A.Y = A.Y - DeltaY();
		return result;
	}

	public PointM Vector()
	{
		PointM result = default;
		result.Assign(DeltaX(), DeltaY(), DeltaZ(), DeltaM());
		return result;
	}

	public PointM UnitVector()
	{
		return Vector().UnitVector();
	}

	public PointM PerpendicularUnitVector()
	{
		double deltaX = DeltaX();
		double deltaY = DeltaY();
		PointM result = default;
		if (deltaX == 0.0)
		{
			result.Assign(System.Math.Sign(0.0 - deltaY), 0.0, 0.0, 0.0);
		}
		else
		{
			if (deltaY != 0.0)
			{
				PointM pointM = default;
				pointM.Assign(0.0 - deltaY, deltaX, 0.0, 0.0);
				return pointM.UnitVector();
			}
			result.Assign(0.0, System.Math.Sign(deltaX), 0.0, 0.0);
		}
		return result;
	}

	public Polygon GetOffsetPolygon(float width)
	{
		PointMs pointMs = new PointMs(4);
		SegmentM segmentM = Offset(width);
		SegmentM segmentM2 = Offset(0f - width);
		pointMs.Add(segmentM.A);
		pointMs.Add(segmentM.B);
		pointMs.Add(segmentM2.B);
		pointMs.Add(segmentM2.A);
		return new Polygon(pointMs);
	}

	public Polygon GetOffsetPolygon(float distLeft, float distRight)
	{
		PointMs pointMs = new PointMs(4);
		SegmentM segmentM = Offset(distLeft);
		SegmentM segmentM2 = Offset(0f - distRight);
		pointMs.Add(segmentM.A);
		pointMs.Add(segmentM.B);
		pointMs.Add(segmentM2.B);
		pointMs.Add(segmentM2.A);
		return new Polygon(pointMs);
	}

	public void GetOffsetPolygons(float distLeft, float distRight, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		PointMs pointMs = new PointMs(4);
		SegmentM segmentM = Offset(distLeft);
		SegmentM segmentM2 = Offset(0f - distRight);
		pointMs.Add(segmentM.A);
		pointMs.Add(segmentM.B);
		pointMs.Add(B);
		pointMs.Add(A);
		leftPolygon = new Polygon(pointMs);
		pointMs = new PointMs(4);
		pointMs.Add(A);
		pointMs.Add(B);
		pointMs.Add(segmentM2.B);
		pointMs.Add(segmentM2.A);
		rightPolygon = new Polygon(pointMs);
	}

	public void GetOffsetPolygonsExtended(float distLeft, float distRight, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		SegmentM segmentM = this;
		segmentM.Extend(distLeft);
		SegmentM segmentM2 = this;
		segmentM2.Extend(distRight);
		SegmentM segmentM3 = segmentM.Offset(distLeft);
		SegmentM segmentM4 = segmentM2.Offset(0f - distRight);
		PointMs pointMs = new PointMs(4);
		pointMs.Add(segmentM3.A);
		pointMs.Add(segmentM3.B);
		pointMs.Add(segmentM.B);
		pointMs.Add(segmentM.A);
		leftPolygon = new Polygon(pointMs);
		pointMs = new PointMs(4);
		pointMs.Add(segmentM2.A);
		pointMs.Add(segmentM2.B);
		pointMs.Add(segmentM4.B);
		pointMs.Add(segmentM4.A);
		rightPolygon = new Polygon(pointMs);
	}

	public void GetOffsetPolygonsExtendedTrapezoidal(float distLeft, float distRight, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		SegmentM segmentM = this;
		segmentM.Extend(distLeft);
		SegmentM segmentM2 = this;
		segmentM2.Extend(distRight);
		SegmentM segmentM3 = segmentM.Offset(distLeft);
		SegmentM segmentM4 = segmentM2.Offset(0f - distRight);
		PointMs pointMs = new PointMs(4);
		pointMs.Add(segmentM3.A);
		pointMs.Add(segmentM3.B);
		pointMs.Add(B);
		pointMs.Add(A);
		leftPolygon = new Polygon(pointMs);
		pointMs = new PointMs(4);
		pointMs.Add(A);
		pointMs.Add(B);
		pointMs.Add(segmentM4.B);
		pointMs.Add(segmentM4.A);
		rightPolygon = new Polygon(pointMs);
	}

	public SegmentM PerpendicularSegment()
	{
		double deltaXTerm = DeltaX() * 0.5;
		double deltaYTerm = DeltaY() * 0.5;
		PointM pointM = MidPoint();
		SegmentM result = default;
		result.A.Assign(pointM.X - deltaYTerm, pointM.Y + deltaXTerm);
		result.B.Assign(pointM.X + deltaYTerm, pointM.Y - deltaXTerm);
		return result;
	}

	public SegmentM PerpendicularSegment(PointM point)
	{
		double deltaXTerm = DeltaX() * 0.5;
		double deltaYTerm = DeltaY() * 0.5;
		SegmentM result = default;
		result.A.Assign(point.X - deltaYTerm, point.Y + deltaXTerm);
		result.B.Assign(point.X + deltaYTerm, point.Y - deltaXTerm);
		return result;
	}

	public SegmentM PerpendicularSegment(double newLength)
	{
		double deltaXTerm = DeltaX() * 0.5;
		double deltaYTerm = DeltaY() * 0.5;
		double newLengthOverLength = newLength / Length;
		deltaXTerm *= newLengthOverLength;
		deltaYTerm *= newLengthOverLength;
		PointM pointM = MidPoint();
		SegmentM result = default;
		result.A.Assign(pointM.X - deltaYTerm, pointM.Y + deltaXTerm);
		result.B.Assign(pointM.X + deltaYTerm, pointM.Y - deltaXTerm);
		return result;
	}

	public void MoveMidpointTo(PointM point)
	{
		PointM pointM = MidPoint();
		Move(point.X - pointM.X, point.Y - pointM.Y);
	}

	public void Move(double dx, double dy)
	{
		A.X += dx;
		A.Y += dy;
		B.X += dx;
		B.Y += dy;
	}

	public PointM AveragedUnitSegment(SegmentM segOther)
	{
		return new PointM(DeltaX() + segOther.DeltaX(), DeltaY() + segOther.DeltaY()).UnitVector();
	}

	public void ToImmediateWindow()
	{
	}

	public override string ToString()
	{
		return A.ToCSString() + " to " + B.ToCSString();
	}
}
