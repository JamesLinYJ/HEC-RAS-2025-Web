using System;

namespace Geospatial.Vectors;

public struct LineIntersection : IEquatable<LineIntersection>
{
	public static LineIntersection NullIntersection = new LineIntersection(Point.NullPoint, -1, double.NaN, -1, double.NaN);

	public readonly PointOnPolyline AIntersection;

	public readonly PointOnPolyline BIntersection;

	public bool IsNull => Point.IsNull();

	public Point Point => AIntersection.Point;

	public LineIntersection(Point pt, int segIdxA, double fracAlongA, int segIdxB, double fracAlongB)
	{
		AIntersection = new PointOnPolyline(pt, segIdxA, fracAlongA);
		BIntersection = new PointOnPolyline(pt, segIdxB, fracAlongB);
	}

	public LineIntersection(double x, double y, int segIdxA, double fracAlongA, int segIdxB, double fracAlongB)
		: this(new Point(x, y), segIdxA, fracAlongA, segIdxB, fracAlongB)
	{
	}

	public LineIntersection(PointOnPolyline popA, PointOnPolyline popB)
	{
		AIntersection = popA;
		BIntersection = popB;
	}

	public LineIntersection SwapAB()
	{
		return new LineIntersection(BIntersection, AIntersection);
	}

	public static int CompareA(LineIntersection int1, LineIntersection int2)
	{
		if (int1.AIntersection.SegmentIndex < int2.AIntersection.SegmentIndex)
		{
			return -1;
		}
		if (int1.AIntersection.SegmentIndex > int2.AIntersection.SegmentIndex)
		{
			return 1;
		}
		if (int1.AIntersection.Fraction < int2.AIntersection.Fraction)
		{
			return -1;
		}
		if (int1.AIntersection.Fraction > int2.AIntersection.Fraction)
		{
			return 1;
		}
		return 0;
	}

	public static int CompareB(LineIntersection int1, LineIntersection int2)
	{
		if (int1.BIntersection.SegmentIndex < int2.BIntersection.SegmentIndex)
		{
			return -1;
		}
		if (int1.BIntersection.SegmentIndex > int2.BIntersection.SegmentIndex)
		{
			return 1;
		}
		if (int1.BIntersection.Fraction < int2.BIntersection.Fraction)
		{
			return -1;
		}
		if (int1.BIntersection.Fraction > int2.BIntersection.Fraction)
		{
			return 1;
		}
		return 0;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(AIntersection, BIntersection);
	}

	public bool Equals(LineIntersection other)
	{
		if (AIntersection.Equals(other.AIntersection))
		{
			return BIntersection.Equals(other.BIntersection);
		}
		return false;
	}
}
