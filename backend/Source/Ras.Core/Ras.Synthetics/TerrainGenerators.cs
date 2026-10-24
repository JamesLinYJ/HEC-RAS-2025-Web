using System;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public static class TerrainGenerators
{
	public static IResample<float> EggCarton(Extent ext)
	{
		return new FunctionalResampler<float>(ext, -9999f, (Point p) =>
		{
			double computed = Math.Sin(p.X);
			double computed2 = Math.Sin(p.Y);
			double computed3 = -1.0 * Math.Abs(computed * computed2);
			double computedTerm = -0.2 * Math.Abs(computed);
			return (float)Math.Min(val2: -0.2 * Math.Abs(computed2), val1: Math.Min(computed3, computedTerm));
		});
	}
}
