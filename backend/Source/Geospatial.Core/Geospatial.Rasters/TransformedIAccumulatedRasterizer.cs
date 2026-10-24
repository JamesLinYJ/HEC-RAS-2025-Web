using System;
using Geospatial.Transformers;

namespace Geospatial.Rasters;

internal class TransformedIAccumulatedRasterizer : TransformedIAccumulatedResampler, IAccumulatedRasterizer<float>, IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	private IAccumulatedRasterizer<float> _rasterizer;

	public RasterDefinition RasterDefinition
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public TransformedIAccumulatedRasterizer(IAccumulatedRasterizer<float> rasterizer, ISpatialTransformer transformer)
		: base(rasterizer, transformer)
	{
		_rasterizer = rasterizer;
	}
}
