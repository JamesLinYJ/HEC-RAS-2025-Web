using System;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

public class InterpolatedBandedResampler : IMultiBandResampler<float>, ISurface<float>
{
	private readonly IResample<float> _fromResampler;

	private readonly IResample<float> _toResampler;

	public int ProfileCount { get; }

	public Extent ValidExtent => _fromResampler.ValidExtent.Intersection(_toResampler.ValidExtent);

	public float? NoDataValue => _fromResampler.NoDataValue;

	public InterpolatedBandedResampler(IResample<float> fromResampler, IResample<float> toResampler, int pfCount)
	{
		_fromResampler = fromResampler;
		_toResampler = toResampler;
		ProfileCount = pfCount;
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		if (bandIndex == 0)
		{
			_fromResampler.Resample(args, outData);
			return;
		}
		if (bandIndex == ProfileCount - 1)
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
			float bandIndexOverProfileCountOffset = (float)bandIndex / (float)(ProfileCount - 1);
			Span<float> outDataSpan = outData.Span;
			Span<float> memSpan = mem.Span;
			for (int i = 0; i < outDataSpan.Length; i++)
			{
				float outData2 = outDataSpan[i];
				float mem2 = memSpan[i];
				if (outData2 == valueOrDefault || mem2 == valueOrDefault)
				{
					outDataSpan[i] = valueOrDefault;
					continue;
				}
				float mem2AboveOutData2 = mem2 - outData2;
				outDataSpan[i] = outData2 + mem2AboveOutData2 * bandIndexOverProfileCountOffset;
			}
		}
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		throw new NotImplementedException();
	}
}
