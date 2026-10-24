using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[DebuggerDisplay("({Point.X}, {Point.Y})")]
[ComVisible(false)]
public class PointIntersect
{
	private PointM _pointM;

	private int _ASegmentIndex;

	private double _AFraction;

	private int _BSegmentIndex;

	private double _BFraction;

	public PointM Point => _pointM;

	public PointOnPolyline APointOnPolyline => new PointOnPolyline(_pointM, _ASegmentIndex, _AFraction);

	public PointOnPolyline BPointOnPolyline => new PointOnPolyline(_pointM, _BSegmentIndex, _BFraction);

	public int ASegmentIndex => _ASegmentIndex;

	public int BSegmentIndex => _BSegmentIndex;

	public double AFraction => _AFraction;

	public double BFraction => _BFraction;

	public PointIntersect(double x, double y, int ASegmentIndex, double AFraction, int BSegmentIndex, double BFraction)
	{
		_pointM = new PointM(x, y);
		_ASegmentIndex = ASegmentIndex;
		_AFraction = AFraction;
		_BSegmentIndex = BSegmentIndex;
		_BFraction = BFraction;
	}

	public static int CompareA(PointIntersect pi1, PointIntersect pi2)
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

	public static int CompareB(PointIntersect pi1, PointIntersect pi2)
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
}
