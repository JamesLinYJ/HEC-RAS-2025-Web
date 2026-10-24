using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public struct PolylineIntersection
{
	public readonly PointM Point;

	public readonly int APolylineIndex;

	public readonly int ASegmentIndex;

	public readonly double AFraction;

	public readonly int BPolylineIndex;

	public readonly int BSegmentIndex;

	public readonly double BFraction;

	public PointOnPolyline APointOnPolyline => new PointOnPolyline(Point, ASegmentIndex, AFraction);

	public PointOnPolyline BPointOnPolyline => new PointOnPolyline(Point, BSegmentIndex, BFraction);

	public PolylineIntersection(PointM point, int aPolylineIndex, int aSegmentIndex, double aFraction, int bPolylineIndex, int bSegmentIndex, double bFraction)
	{
		this = default;
		Point = point;
		APolylineIndex = aPolylineIndex;
		ASegmentIndex = aSegmentIndex;
		AFraction = aFraction;
		BPolylineIndex = bPolylineIndex;
		BSegmentIndex = bSegmentIndex;
		BFraction = bFraction;
	}

	public static int CompareA(PolylineIntersection pi1, PolylineIntersection pi2)
	{
		if (pi1.APolylineIndex < pi2.APolylineIndex)
		{
			return -1;
		}
		if (pi1.APolylineIndex == pi2.APolylineIndex)
		{
			if (pi1.ASegmentIndex < pi2.ASegmentIndex)
			{
				return -1;
			}
			if (pi1.ASegmentIndex == pi2.ASegmentIndex)
			{
				if (pi1.AFraction < pi2.AFraction)
				{
					return -1;
				}
				if (pi1.AFraction == pi2.AFraction)
				{
					return 0;
				}
				return 1;
			}
			return 1;
		}
		return 1;
	}

	public static int CompareB(PolylineIntersection pi1, PolylineIntersection pi2)
	{
		if (pi1.BPolylineIndex < pi2.BPolylineIndex)
		{
			return -1;
		}
		if (pi1.BPolylineIndex == pi2.BPolylineIndex)
		{
			if (pi1.BSegmentIndex < pi2.BSegmentIndex)
			{
				return -1;
			}
			if (pi1.BSegmentIndex == pi2.BSegmentIndex)
			{
				if (pi1.BFraction < pi2.BFraction)
				{
					return -1;
				}
				if (pi1.BFraction == pi2.BFraction)
				{
					return 0;
				}
				return 1;
			}
			return 1;
		}
		return 1;
	}
}
