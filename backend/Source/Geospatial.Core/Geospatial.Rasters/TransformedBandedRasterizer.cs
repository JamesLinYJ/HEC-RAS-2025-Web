using System;
using Geospatial.Transformers;

namespace Geospatial.Rasters;

internal class TransformedBandedRasterizer<T> : TransformedBandedResampler<T>, IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandRaster<T> _baseRes;

	public RasterDefinition RasterDefinition { get; }

	public TransformedBandedRasterizer(IMultiBandRaster<T> baseRes, ISpatialTransformer transformer)
		: base((IMultiBandResampler<T>)baseRes, transformer)
	{
		_baseRes = baseRes;
		RasterDefinition = _baseRes.RasterDefinition.Transform(transformer);
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> outData)
	{
		_baseRes.Resample(new ResampleArgs(RasterDefinition.GetSubRaster(range)), bandIndex, outData);
	}
}
