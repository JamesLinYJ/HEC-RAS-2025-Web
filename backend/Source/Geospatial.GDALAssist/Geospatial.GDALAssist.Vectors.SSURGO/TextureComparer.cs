using System.Collections.Generic;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class TextureComparer : IEqualityComparer<Texture>
{
	public bool Equals(Texture x, Texture y)
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

	public int GetHashCode(Texture obj)
	{
		return obj.GetKeylessHashCode();
	}
}
