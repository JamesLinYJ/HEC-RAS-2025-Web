using System;
using Geospatial.Transformers;

namespace Geospatial.Rasters;

internal class TransformedVelocityRasterizer : TransformedVelocityResampler, ITimeSeriesVelocityRasterizer<float>, ITimeSeriesVelocityResampler<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	private readonly ITimeSeriesVelocityRasterizer<float> _rasterizer;

	public RasterDefinition RasterDefinition => _rasterizer.RasterDefinition.Transform(_transformer);

	public TransformedVelocityRasterizer(ITimeSeriesVelocityRasterizer<float> rasterizer, ISpatialTransformer transformer, VelocityTransformOptions opts, IValueTransformer<float> magnitudeTransformer, IValueTransformer<float> directionTransformer)
		: base(rasterizer, transformer, opts, magnitudeTransformer, directionTransformer)
	{
		_rasterizer = rasterizer;
	}

	public void GetRangeVxVy(PixelRange range, int bandIndex, Memory<float> vxResult, Memory<float> vyResult, Memory<float> magResult)
	{
		throw new NotImplementedException();
	}

	public void GetRangeMagDir(PixelRange range, int bandIndex, Memory<float> magResult, Memory<float> dirResult)
	{
		throw new NotImplementedException();
	}
}
