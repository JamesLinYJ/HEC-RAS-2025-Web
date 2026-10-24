using System;
using Geospatial.Rasters;

namespace Ras.Mapping;

public interface IStaticMapComputer
{
	int ProfileCount { get; }

	StaticCache GetStaticCache(RasterDefinition rasterDefinition);

	void AccumulateStaticMap(int pfIdx, StaticCache staticCache, Memory<float> output);
}
