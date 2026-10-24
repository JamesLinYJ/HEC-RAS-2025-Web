using System.Collections.Generic;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public class TextureGroupComparer : IEqualityComparer<TextureGroup>
{
	public bool Equals(TextureGroup x, TextureGroup y)
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

	public int GetHashCode(TextureGroup obj)
	{
		return obj.GetKeylessHashCode();
	}
}
