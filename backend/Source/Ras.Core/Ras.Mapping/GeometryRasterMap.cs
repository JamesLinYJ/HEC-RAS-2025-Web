using Geospatial.Rasters;
using Ras.Layers;

namespace Ras.Mapping;

public class GeometryRasterMap
{
	public RasterDefinition Raster { get; }

	public Geometry Geometry { get; }

	public GeometryRasterMap(Geometry geom, RasterDefinition raster)
	{
		Raster = raster;
		Geometry = geom;
	}
}
