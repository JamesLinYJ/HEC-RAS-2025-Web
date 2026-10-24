using System;
using Geospatial.Transformers;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class ValueTransformedResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly IResample<T> _resampler;

	protected readonly IValueTransformer<T> _transformer;

	public Extent ValidExtent => _resampler.ValidExtent;

	public T? NoDataValue => _resampler.NoDataValue;

	public ValueTransformedResampler(IResample<T> resampler, IValueTransformer<T> transformer)
	{
		_resampler = resampler;
		_transformer = transformer;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		_resampler.Resample(args, outData);
		_transformer.Transform(outData, NoDataValue);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_resampler.SamplePoints(points, outData, sampleParams);
		_transformer.Transform(outData, NoDataValue);
	}
}
