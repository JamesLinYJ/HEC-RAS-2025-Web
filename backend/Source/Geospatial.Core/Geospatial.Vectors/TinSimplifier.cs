using System;

namespace Geospatial.Vectors;

public static class TinSimplifier
{
	public static Tin SimplifyByZ(Tin tin)
	{
		if (!tin.HasZ)
		{
			throw new ArgumentException("Tin needs to track Z values to simplify by Z");
		}
		throw new NotImplementedException();
	}
}
