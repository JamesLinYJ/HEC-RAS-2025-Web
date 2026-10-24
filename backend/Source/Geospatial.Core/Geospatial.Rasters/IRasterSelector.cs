using Geospatial.Vectors;

namespace Geospatial.Rasters;

public interface IRasterSelector
{
	RasterDefinition GetAppropriateResampleRaster(Extent ext);

	RasterDefinition GetAppropriateResampleRaster(Polygon poly);
}
