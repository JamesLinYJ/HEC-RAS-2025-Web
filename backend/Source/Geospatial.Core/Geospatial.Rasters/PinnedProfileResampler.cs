using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class PinnedProfileResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandResampler<T> _ts;

	private readonly int _pfIdx;

	public Extent ValidExtent => _ts.ValidExtent;

	public T? NoDataValue => _ts.NoDataValue;

	public PinnedProfileResampler(IMultiBandResampler<T> timeSeries, int pf)
	{
		_ts = timeSeries;
		_pfIdx = pf;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		_ts.Resample(args, _pfIdx, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_ts.SamplePoints(points, _pfIdx, outData);
	}
}
