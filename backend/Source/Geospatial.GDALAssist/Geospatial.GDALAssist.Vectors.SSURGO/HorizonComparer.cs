using System.Collections.Generic;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class HorizonComparer : IEqualityComparer<Horizon>
{
	public bool Equals(Horizon x, Horizon y)
	{
		if (x == null && y == null)
		{
			return true;
		}
		if (x == null || y == null)
		{
			return false;
		}
		return x.FunctionallyEquals(y);
	}

	public int GetHashCode(Horizon obj)
	{
		return obj.GetKeylessHashCode();
	}
}
