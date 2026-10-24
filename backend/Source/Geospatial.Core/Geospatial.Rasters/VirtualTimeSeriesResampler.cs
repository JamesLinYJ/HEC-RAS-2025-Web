using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rasters;

internal class VirtualTimeSeriesResampler<T> : ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, ICacheEviction where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandResampler<T> _banded;

	public bool CanInterpolateTimesteps => false;

	public int ProfileCount => Times.Count;

	public Extent ValidExtent => _banded.ValidExtent;

	public T? NoDataValue => _banded.NoDataValue;

	public IReadOnlyList<DateTime> Times { get; }

	public VirtualTimeSeriesResampler(IMultiBandResampler<T> banded, IList<DateTime> timesteps)
	{
		_banded = banded;
		Times = timesteps.AsReadOnly();
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		int binarySearch = MathHelpers.BinarySearch(Times, time);
		if (binarySearch < 0)
		{
			throw new Exception("Cannot interpolate timesteps on this object.");
		}
		_banded.Resample(args, binarySearch, outData, cache);
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		_banded.Resample(args, profileIndex, outData, cache);
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outData)
	{
		_banded.SamplePoints(points, bandStart, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> outData)
	{
		_banded.SamplePoints(points, profileIndex, outData);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}

	public void EvictCache()
	{
		(_banded as ICacheEviction)?.EvictCache();
	}

	public IDisposable TryPauseCache()
	{
		return (_banded as ICacheEviction)?.TryPauseCache() ?? NoOpDisposable.Instance;
	}
}
