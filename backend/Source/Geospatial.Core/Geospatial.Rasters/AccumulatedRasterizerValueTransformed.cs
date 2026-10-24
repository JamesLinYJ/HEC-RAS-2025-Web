using Utility.Units;

namespace Geospatial.Rasters;

internal class AccumulatedRasterizerValueTransformed : AccumulatedResamplerValueTransformed, IAccumulatedRasterizer<float>, IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	private readonly IAccumulatedRasterizer<float> _rasterizer;

	public RasterDefinition RasterDefinition => _rasterizer.RasterDefinition;

	public AccumulatedRasterizerValueTransformed(IAccumulatedRasterizer<float> rasterizer, ConverterDelegates.ConvertInPlaceNDV<float> transformer, Time newTimeUnits)
		: base(rasterizer, transformer, newTimeUnits)
	{
		_rasterizer = rasterizer;
	}
}
