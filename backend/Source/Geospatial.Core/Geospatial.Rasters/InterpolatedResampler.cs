using System;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class InterpolatedResampler : IResample<float>, ISurface<float>
{
	private readonly float _ratio;

	private readonly IResample<float> _fromResampler;

	private readonly IResample<float> _toResampler;

	public Extent ValidExtent => _fromResampler.ValidExtent.Intersection(_toResampler.ValidExtent);

	public float? NoDataValue => _fromResampler.NoDataValue;

	public InterpolatedResampler(IResample<float> fromResampler, IResample<float> toResampler, float ratio)
	{
		_fromResampler = fromResampler;
		_toResampler = toResampler;
		_ratio = ratio;
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		if (_ratio == 0f)
		{
			_fromResampler.Resample(args, outData);
			return;
		}
		if (_ratio == 1f)
		{
			_toResampler.Resample(args, outData);
			return;
		}
		_fromResampler.Resample(args, outData);
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(outData.Length, valueOrDefault, out mem))
		{
			_toResampler.Resample(args, mem);
			Buffers.LinearInterpolate(outData.Span, mem.Span, outData.Span, _ratio, NoDataValue.GetValueOrDefault());
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		if (_ratio == 0f)
		{
			_fromResampler.SamplePoints(points, outData, sampleParams);
			return;
		}
		if (_ratio == 1f)
		{
			_toResampler.SamplePoints(points, outData, sampleParams);
			return;
		}
		_fromResampler.SamplePoints(points, outData, sampleParams);
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(outData.Length, valueOrDefault, out mem))
		{
			_toResampler.SamplePoints(points, mem, sampleParams);
			Buffers.LinearInterpolate(outData.Span, mem.Span, outData.Span, _ratio, NoDataValue.GetValueOrDefault());
		}
	}
}
