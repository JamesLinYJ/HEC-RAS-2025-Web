using System;
using Geospatial.Rasters;
using Ras.Layers;

namespace Ras.Mapping;

public class StaticCache
{
	public GeometryMapPoints Geometry { get; }

	public Memory<float> Terrain { get; }

	public StaticCache(Geometry geo, IResample<float> terrain, RasterDefinition def)
	{
		Geometry = geo.RasterizeForMapping(def);
		Terrain = new float[def.CellCount];
		terrain.Resample(new ResampleArgs(def), Terrain);
	}
}
