using Geospatial.Rasters;

namespace Geospatial.Rendering.Layers;

public static class RenderLayerExtensions
{
	public static DiscreteRasterRenderLayer AsRenderLayer(this IResample<int> res)
	{
		return new DiscreteRasterRenderLayer(res);
	}
}
