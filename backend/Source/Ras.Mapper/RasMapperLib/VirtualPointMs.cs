namespace RasMapperLib;

public class VirtualPointMs : IPoints
{
	private int _count;

	private double[] _x;

	private double[] _y;

	public int Count => _count;

	public VirtualPointMs(int count, double[] x, double[] y)
	{
		_count = count;
		_x = x;
		_y = y;
	}

	public PointM FirstPointM()
	{
		return new PointM(_x[0], _y[0]);
	}

	PointM IPoints.FirstPointM()
	{
		//ILSpy generated this explicit interface implementation from .override directive in FirstPointM
		return this.FirstPointM();
	}

	public PointM LastPointM()
	{
		int countOffset = checked(_count - 1);
		return new PointM(_x[countOffset], _y[countOffset]);
	}

	PointM IPoints.LastPointM()
	{
		//ILSpy generated this explicit interface implementation from .override directive in LastPointM
		return this.LastPointM();
	}

	public PointM PointM(int index)
	{
		return new PointM(_x[index], _y[index]);
	}

	PointM IPoints.PointM(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in PointM
		return this.PointM(index);
	}

	public Extent ComputeExtent()
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		checked
		{
			int count = _count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = PointM(i);
				if (pointM.X < maxValue)
				{
					maxValue = pointM.X;
				}
				if (pointM.X > minValue)
				{
					minValue = pointM.X;
				}
				if (pointM.Y < maxValue2)
				{
					maxValue2 = pointM.Y;
				}
				if (pointM.Y > minValue2)
				{
					minValue2 = pointM.Y;
				}
			}
			return new Extent(minValue, maxValue, minValue2, maxValue2);
		}
	}

	Extent IPoints.ComputeExtent()
	{
		//ILSpy generated this explicit interface implementation from .override directive in ComputeExtent
		return this.ComputeExtent();
	}

	public int PointMCount()
	{
		return _count;
	}

	int IPoints.PointMCount()
	{
		//ILSpy generated this explicit interface implementation from .override directive in PointMCount
		return this.PointMCount();
	}
}
