namespace Geospatial.Rendering.Layers;

public struct RasterLayerSymbologyOptions
{
	public const int DefaultMaxBytesToSample = 67108864;

	public int MaxBytesToSample;

	public static RasterLayerSymbologyOptions Default()
	{
		return new RasterLayerSymbologyOptions
		{
			MaxBytesToSample = 67108864
		};
	}
}
