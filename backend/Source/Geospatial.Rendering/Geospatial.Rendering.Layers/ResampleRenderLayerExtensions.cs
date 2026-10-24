using Geospatial.Rasters;

namespace Geospatial.Rendering.Layers;

public static class ResampleRenderLayerExtensions
{
	public static RasterRenderLayer AsRenderLayer(this IResample<float> lyr)
	{
		return new RasterRenderLayer(lyr);
	}
}
