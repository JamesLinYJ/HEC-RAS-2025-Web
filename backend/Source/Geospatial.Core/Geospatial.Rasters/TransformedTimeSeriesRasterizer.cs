using System;
using Geospatial.Transformers;

namespace Geospatial.Rasters;

internal class TransformedTimeSeriesRasterizer : TransformedTimeSeriesResampler, ITimeSeriesRaster<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps, IMultiBandRaster<float>, IRasterDefinition
{
	private readonly ITimeSeriesRaster<float> _rasterizer;

	public RasterDefinition RasterDefinition
	{
		get
		{
			if (_spatialTransformer != null)
			{
				return _rasterizer.RasterDefinition.Transform(_spatialTransformer);
			}
			return _rasterizer.RasterDefinition;
		}
	}

	public TransformedTimeSeriesRasterizer(ITimeSeriesRaster<float> rasterizer, ISpatialTransformer spatialTransformer, IValueTransformer<float> valueTransformer)
		: base(rasterizer, spatialTransformer, valueTransformer)
	{
		_rasterizer = rasterizer;
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<float> result)
	{
		ResampleArgs args = new ResampleArgs(RasterDefinition.GetSubRaster(range));
		// Resample already applies the value transformer. Block reads must use
		// the selected band and convert once, just like point/date sampling.
		Resample(args, bandIndex, result);
	}
}
