using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{ToString()}")]
public struct Circle : IExtent
{
	public Point Center;

	public double Radius;

	public double X => Center.X;

	public double Y => Center.Y;

	public Extent Extent => new Extent(MinX, MaxX, MinY, MaxY);

	public double MaxX => Center.X + Radius;

	public double MinX => Center.X - Radius;

	public double MaxY => Center.Y + Radius;

	public double MinY => Center.Y - Radius;

	public Circle(double x, double y, double rad)
	{
		Center = new Point(x, y);
		Radius = rad;
	}

	public Circle(Point c, double rad)
	{
		Center = c;
		Radius = rad;
	}

	public bool Equals(Circle circ)
	{
		if (circ.Radius == Radius)
		{
			return circ.Center == Center;
		}
		return false;
	}

	public static bool IsValid(Circle circ)
	{
		if (!double.IsNaN(circ.Center.X) && !double.IsNaN(circ.Center.Y))
		{
			return !double.IsNaN(circ.Radius);
		}
		return false;
	}

	public bool Contains(Point ptm)
	{
		if (ptm.DistanceTo(Center) <= Radius)
		{
			return true;
		}
		return false;
	}

	public bool Contains(IPoint ptm)
	{
		if (ptm.DistanceTo(Center) <= Radius)
		{
			return true;
		}
		return false;
	}

	public double PerimeterDistanceToPoint(Point ptm)
	{
		return Math.Abs(Center.DistanceTo(ptm) - Radius);
	}

	public static bool Intersects(Point centerCircle, double radius, Extent extent)
	{
		return new Circle(centerCircle, radius).Overlaps(extent);
	}

	public bool Overlaps(Extent extent)
	{
		if (MaxX <= extent.MinX)
		{
			return false;
		}
		if (MinX >= extent.MaxX)
		{
			return false;
		}
		if (MaxY <= extent.MinY)
		{
			return false;
		}
		if (MinY >= extent.MaxY)
		{
			return false;
		}
		if (extent.Contains(Center))
		{
			return true;
		}
		double x = Center.X;
		double y = Center.Y;
		if (x <= extent.MinX)
		{
			if (y >= extent.MaxY)
			{
				return Contains(extent.UpperLeft);
			}
			if (y <= extent.MinY)
			{
				return Contains(extent.LowerLeft);
			}
			return MaxX > extent.MinX;
		}
		if (x >= extent.MaxX)
		{
			if (y >= extent.MaxY)
			{
				return Contains(extent.UpperRight);
			}
			if (y <= extent.MinY)
			{
				return Contains(extent.LowerRight);
			}
			return MinX < extent.MaxX;
		}
		if (y >= extent.MaxY)
		{
			return MinY < extent.MaxY;
		}
		if (y <= extent.MinY)
		{
			return MaxY > extent.MinY;
		}
		return true;
	}

	public bool IntersectsSegment(Segment seg)
	{
		Point @int;
		double t;
		Point int2;
		double t2;
		return IntersectsSegment(seg, out @int, out t, out int2, out t2);
	}

	public bool IntersectsSegment(Segment seg, out Point int1, out double t1, out Point int2, out double t2)
	{
		Segment segment = seg;
		seg.Extend(Radius * 2.1);
		double length = seg.Length;
		double x = seg.A.X;
		double y = seg.A.Y;
		double x2 = Center.X;
		double y2 = Center.Y;
		double radius = Radius;
		double deltaXOverLength = seg.DeltaX / length;
		double deltaYOverLength = seg.DeltaY / length;
		double x2Y2Term = deltaXOverLength * (x2 - x) + deltaYOverLength * (y2 - y);
		double computed = x2Y2Term * deltaXOverLength + x;
		double computed2 = x2Y2Term * deltaYOverLength + y;
		double x2Y2Term2 = Math.Sqrt((computed - x2) * (computed - x2) + (computed2 - y2) * (computed2 - y2));
		if (x2Y2Term2 < radius)
		{
			double radiusTerm = Math.Sqrt(radius * radius - x2Y2Term2 * x2Y2Term2);
			double computed3 = x2Y2Term - radiusTerm;
			double x3 = computed3 * deltaXOverLength + x;
			double y3 = computed3 * deltaYOverLength + y;
			int1 = new Point(x3, y3);
			t1 = segment.FractionAlong(int1);
			double computed4 = x2Y2Term + radiusTerm;
			double x4 = computed4 * deltaXOverLength + x;
			double y4 = computed4 * deltaYOverLength + y;
			int2 = new Point(x4, y4);
			t2 = segment.FractionAlong(int2);
			return true;
		}
		int1 = Point.NullPoint;
		int2 = Point.NullPoint;
		t1 = double.NaN;
		t2 = double.NaN;
		return false;
	}

	public bool MBROverlaps(IExtent other)
	{
		return Extent.MBROverlaps(other);
	}

	public bool Contains(Polyline pline)
	{
		new List<Tuple<double, double>>();
		if (!Polyline.IsValidPolyline(pline))
		{
			return false;
		}
		if (!MBROverlaps(pline.Extent))
		{
			return false;
		}
		for (int i = 0; i < pline.PointCount; i++)
		{
			Point ptm = pline[i];
			if (!Contains(ptm))
			{
				return false;
			}
		}
		return true;
	}

	public List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline)
	{
		List<Tuple<double, double>> containedSubsegments = new List<Tuple<double, double>>();
		if (!Polyline.IsValidPolyline(polyline))
		{
			return containedSubsegments;
		}
		double[] cumulativeSegmentLengths = polyline.CumulativeSegmentLengths();
		double cumulativeSegmentLengthSum = 0.0;
		Segment segment = polyline.Segments[0];
		Point ptm = segment.A;
		double tolerance = 1E-05;
		if (PerimeterDistanceToPoint(ptm) < tolerance)
		{
			ptm = segment.PointAtDistance(tolerance);
		}
		bool condition;
		if (Contains(ptm))
		{
			condition = true;
			cumulativeSegmentLengthSum = 0.0;
		}
		else
		{
			condition = false;
		}
		for (int i = 0; i < polyline.SegmentCount; i++)
		{
			Segment seg = polyline.Segments[i];
			if (!IntersectsSegment(seg, out var _, out var t, out var _, out var t2))
			{
				continue;
			}
			if (condition)
			{
				if (t > 0.0 && t < 1.0)
				{
					throw new NotImplementedException();
				}
				if (t2 > 0.0 && t2 < 1.0)
				{
					double cumulativeSegmentLengthSum2 = seg.Length * t2;
					if (i != 0)
					{
						cumulativeSegmentLengthSum2 += cumulativeSegmentLengths[i - 1];
					}
					containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum2));
					condition = false;
				}
				continue;
			}
			if (t > 0.0 && t < 1.0)
			{
				double cumulativeSegmentLengthSum3 = seg.Length * t;
				if (i != 0)
				{
					cumulativeSegmentLengthSum3 += cumulativeSegmentLengths[i - 1];
				}
				cumulativeSegmentLengthSum = cumulativeSegmentLengthSum3;
				condition = true;
			}
			if (t2 > 0.0 && t2 < 1.0)
			{
				double cumulativeSegmentLengthSum4 = seg.Length * t2;
				if (i != 0)
				{
					cumulativeSegmentLengthSum4 += cumulativeSegmentLengths[i - 1];
				}
				containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum4));
				condition = false;
			}
		}
		if (condition)
		{
			containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, polyline.Length));
		}
		return containedSubsegments;
	}

	public static Circle ComputeCircumCircle(Point A, Point B, Point C)
	{
		Point point = ComputeCenter(A, B, C);
		double rad = A.DistanceTo(point);
		return new Circle(point, rad);
	}

	public static Point ComputeCenter(Point A, Point B, Point C)
	{
		double computed = B.X - A.X;
		double computed2 = B.Y - A.Y;
		double computed3 = computed * computed + computed2 * computed2;
		double computed4 = C.X - A.X;
		double computed5 = C.Y - A.Y;
		double computed6 = computed4 * computed4 + computed5 * computed5;
		double computed7 = 2.0 * (computed * computed5 - computed2 * computed4);
		if (computed7 == 0.0)
		{
			throw new ColinearTriangleException(A, B, C);
		}
		double x = A.X + (computed5 * computed3 - computed2 * computed6) / computed7;
		double y = A.Y + (computed * computed6 - computed4 * computed3) / computed7;
		return new Point(x, y);
	}

	public static Point ComputeCenter(IPoint A, IPoint B, IPoint C)
	{
		double computed = B.X - A.X;
		double computed2 = B.Y - A.Y;
		double computed3 = computed * computed + computed2 * computed2;
		double computed4 = C.X - A.X;
		double computed5 = C.Y - A.Y;
		double computed6 = computed4 * computed4 + computed5 * computed5;
		double computed7 = 2.0 * (computed * computed5 - computed2 * computed4);
		if (computed7 == 0.0)
		{
			throw new ColinearTriangleException(A.ToPoint(), B.ToPoint(), C.ToPoint());
		}
		double x = A.X + (computed5 * computed3 - computed2 * computed6) / computed7;
		double y = A.Y + (computed * computed6 - computed4 * computed3) / computed7;
		return new Point(x, y);
	}

	public override string ToString()
	{
		return "Center: " + Center.ToString() + ", Radius: " + Radius;
	}
}
