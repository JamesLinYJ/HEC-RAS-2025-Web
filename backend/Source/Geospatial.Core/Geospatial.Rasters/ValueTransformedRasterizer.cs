using System;
using Geospatial.Transformers;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class ValueTransformedRasterizer<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IRaster<T> _raster;

	protected readonly IValueTransformer<T> _transformer;

	public Extent ValidExtent => _raster.ValidExtent;

	public T? NoDataValue => _raster.NoDataValue;

	public RasterDefinition RasterDefinition => _raster.RasterDefinition;

	public ValueTransformedRasterizer(IRaster<T> resampler, IValueTransformer<T> transformer)
	{
		_raster = resampler;
		_transformer = transformer;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		_raster.Resample(args, outData);
		_transformer.Transform(outData, NoDataValue);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_raster.SamplePoints(points, outData, sampleParams);
		_transformer.Transform(outData, NoDataValue);
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		_raster.GetRange(range, outData);
		_transformer.Transform(outData, NoDataValue);
	}
}
