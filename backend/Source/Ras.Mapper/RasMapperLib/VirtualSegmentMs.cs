using System.Linq;

namespace RasMapperLib;

public class VirtualSegmentMs : ISegments
{

	private double[] _x;

	private double[] _y;

	public VirtualSegmentMs(double[] x, double[] y)
	{
		_x = x;
		_y = y;
	}

	public SegmentM Segment(int index)
	{
		PointM pA = new PointM(_x[index], _y[index]);
		PointM pB = checked(new PointM(_x[index + 1], _y[index + 1]));
		return new SegmentM(pA, pB, index);
	}

	SegmentM ISegments.Segment(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Segment
		return this.Segment(index);
	}

	public int SegmentCount()
	{
		return checked(_x.Count() - 1);
	}

	int ISegments.SegmentCount()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SegmentCount
		return this.SegmentCount();
	}
}
