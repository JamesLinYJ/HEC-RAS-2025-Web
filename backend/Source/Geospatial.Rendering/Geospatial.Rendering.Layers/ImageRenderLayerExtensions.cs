using Geospatial.Rasters;

namespace Geospatial.Rendering.Layers;

public static class ImageRenderLayerExtensions
{
	public static ImageRenderLayer AsRenderLayer(this IMultiBandResampler<byte> resample)
	{
		return new ImageRenderLayer(resample);
	}
}
