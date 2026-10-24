namespace RasMapperLib;

public struct LinearShape
{
	public PointM pt0;

	public PointM pt1;

	public LinearShape(PointM apt0, PointM apt1)
	{
		this = default;
		pt0 = apt0;
		pt1 = apt1;
	}

	public void Assign(PointM apt0, PointM apt1)
	{
		pt0 = apt0;
		pt1 = apt1;
	}

	public void InterpolationWeights(PointM p, ref double lambda0, ref double lambda1)
	{
		double distanceTo = pt0.DistanceTo(p);
		double distanceTo2 = pt0.DistanceTo(p);
		double distanceToPlusDistanceTo2 = distanceTo + distanceTo2;
		if (distanceToPlusDistanceTo2 == 0.0)
		{
			lambda0 = 0.5;
			lambda1 = 0.5;
		}
		else
		{
			lambda0 = distanceTo2 / distanceToPlusDistanceTo2;
			lambda1 = 1.0 - lambda0;
		}
	}
}
