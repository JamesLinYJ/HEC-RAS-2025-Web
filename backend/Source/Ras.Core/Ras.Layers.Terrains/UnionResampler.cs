using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Ras.Layers.Terrains;

public class UnionResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private IEnumerable<IResample<T>> _resamplers;

	public IEnumerable<IResample<T>> Resamplers => _resamplers;

	public Extent ValidExtent => Extent.Union(_resamplers.Select((IResample<T> r) => r.ValidExtent));

	public T? NoDataValue => _resamplers.First().NoDataValue;

	public UnionResampler(IEnumerable<IResample<T>> resamplers)
	{
		_resamplers = resamplers;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		foreach (IResample<T> resampler in _resamplers)
		{
			resampler.Resample(args, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		foreach (IResample<T> resampler in _resamplers)
		{
			resampler.SamplePoints(points, outData, sampleParams);
		}
	}
}
