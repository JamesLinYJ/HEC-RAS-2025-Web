using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic;

namespace RasMapperLib;

[ComVisible(false)]
public class PointOnPolyline
{
	private PointM _pointM;

	private int _segmentIndex;

	private double _fraction;

	public PointM PointM => _pointM;

	public int SegmentIndex => _segmentIndex;

	public double Fraction => _fraction;

	public PointOnPolyline(double x, double y, int segmentIndex, double fraction)
	{
		_pointM.Assign(x, y);
		_segmentIndex = segmentIndex;
		_fraction = fraction;
	}

	public PointOnPolyline(PointM pointM, int segmentIndex, double fraction)
	{
		_pointM = pointM;
		_segmentIndex = segmentIndex;
		_fraction = fraction;
	}

	public static List<PointOnPolyline> Sort(List<PointOnPolyline> points)
	{
		return (from pop in points
			orderby pop._segmentIndex, pop._fraction
			select pop).ToList();
	}

	public override string ToString()
	{
		return "PointOnPolyline seg=" + Strings.Format(_segmentIndex) + " frac=" + Strings.Format(_fraction) + " pt=" + _pointM.ToString();
	}

	public bool LessThan(PointOnPolyline pop)
	{
		return Compare(this, pop) == -1;
	}

	public bool GreaterThan(PointOnPolyline pop)
	{
		return Compare(this, pop) == 1;
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
}
