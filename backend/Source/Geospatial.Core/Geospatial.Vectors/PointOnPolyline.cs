using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors;

public struct PointOnPolyline : IEquatable<PointOnPolyline>
{
	public class PointOnPolylineComparer : IComparer<PointOnPolyline>
	{
		public int Compare(PointOnPolyline x, PointOnPolyline y)
		{
			return PointOnPolyline.Compare(x, y);
		}
	}

	public static readonly PointOnPolyline NullPointOnPolyline = new PointOnPolyline(Point.NullPoint, -1, double.NaN);

	public Point Point;

	public int SegmentIndex;

	public double Fraction;

	public bool IsValid()
	{
		return IsValid(this);
	}

	public bool IsNull()
	{
		return !IsValid(this);
	}

	public static bool IsValid(PointOnPolyline pop)
	{
		if (pop.SegmentIndex < 0)
		{
			return false;
		}
		if (double.IsNaN(pop.Fraction))
		{
			return false;
		}
		if (pop.Point.IsNull())
		{
			return false;
		}
		return true;
	}

	public PointOnPolyline(double x, double y, int segmentIndex, double fraction)
	{
		Point = new Point(x, y);
		SegmentIndex = segmentIndex;
		Fraction = fraction;
	}

	public PointOnPolyline(Point pt, int segmentIndex, double fraction)
	{
		Point = pt;
		SegmentIndex = segmentIndex;
		Fraction = fraction;
	}

	public bool IsStart(double tolerance = 0.0)
	{
		if (SegmentIndex == 0)
		{
			return Fraction <= 1.0 - tolerance;
		}
		return false;
	}

	public bool IsEnd(int segCount, double tolerance = 0.0)
	{
		if (SegmentIndex == segCount - 1)
		{
			return Fraction >= tolerance;
		}
		return false;
	}

	public static List<PointOnPolyline> Sort(List<PointOnPolyline> points)
	{
		return (from pop in points
			orderby pop.SegmentIndex, pop.Fraction
			select pop).ToList();
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Point, SegmentIndex, Fraction);
	}

	public override string ToString()
	{
		return $"Segment={SegmentIndex}, Fraction={Fraction}, Point={Point}";
	}

	public bool LessThan(PointOnPolyline pop)
	{
		return Compare(this, pop) == -1;
	}

	public bool GreaterThan(PointOnPolyline pop)
	{
		return Compare(this, pop) == 1;
	}

	public bool Equals(PointOnPolyline pop)
	{
		return Compare(this, pop) == 0;
	}

	public static bool operator >(PointOnPolyline p1, PointOnPolyline p2)
	{
		return p1.GreaterThan(p2);
	}

	public static bool operator <(PointOnPolyline p1, PointOnPolyline p2)
	{
		return p1.LessThan(p2);
	}

	public static int Compare(PointOnPolyline pop1, PointOnPolyline pop2)
	{
		if (pop1.SegmentIndex < pop2.SegmentIndex)
		{
			return -1;
		}
		if (pop1.SegmentIndex == pop2.SegmentIndex)
		{
			if (pop1.Fraction < pop2.Fraction)
			{
				return -1;
			}
			if (pop1.Fraction == pop2.Fraction)
			{
				return 0;
			}
			return 1;
		}
		return 1;
	}

	public static IComparer<PointOnPolyline> Comparer()
	{
		return new PointOnPolylineComparer();
	}
}
