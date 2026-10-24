using Geospatial.Rasters;

namespace Geospatial.Rendering.Layers;

public static class IBandedRenderLayerExtensions
{
	public static BandedRenderLayer AsRenderLayer(this IMultiBandResampler<float> brl)
	{
		return new BandedRenderLayer(brl);
	}
}
