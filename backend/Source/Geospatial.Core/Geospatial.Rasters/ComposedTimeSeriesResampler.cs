using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class ComposedTimeSeriesResampler<T> : ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps, IDiscreteTimestepsWithEvent where T : unmanaged, IEquatable<T>
{
	private readonly ITimeSeriesResampler<T> _baseRes;

	private readonly ITimeSeriesResampler<T> _altBandedResampler;

	private readonly ConverterDelegates.Convert<T> _composeInPlace;

	public int ProfileCount => _baseRes.ProfileCount;

	public Extent ValidExtent => _baseRes.ValidExtent;

	public T? NoDataValue => _baseRes.NoDataValue;

	public bool CanInterpolateTimesteps => false;

	public IReadOnlyList<DateTime> Times => _baseRes.Times;

	public event TimestepAddedEvent TimestepAdded
	{
		add
		{
			if (_baseRes is IDiscreteTimestepsWithEvent discreteTimestepsWithEvent)
			{
				discreteTimestepsWithEvent.TimestepAdded += value;
			}
		}
		remove
		{
			if (_baseRes is IDiscreteTimestepsWithEvent discreteTimestepsWithEvent)
			{
				discreteTimestepsWithEvent.TimestepAdded -= value;
			}
		}
	}

	public ComposedTimeSeriesResampler(ITimeSeriesResampler<T> baseRes, ITimeSeriesResampler<T> altResampler, ConverterDelegates.Convert<T> composeInPlace)
	{
		_baseRes = baseRes;
		_altBandedResampler = altResampler;
		_composeInPlace = composeInPlace;
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(args.Raster.CellCount, out mem))
		{
			ResampleCache cache2 = null;
			ResampleCache cache3 = null;
			if (cache != null)
			{
				Tuple<ResampleCache, ResampleCache> tuple = cache.SpatialCache as Tuple<ResampleCache, ResampleCache>;
				if (tuple == null)
				{
					tuple = (Tuple<ResampleCache, ResampleCache>)(cache.SpatialCache = new Tuple<ResampleCache, ResampleCache>(new ResampleCache(), new ResampleCache()));
					cache.TrackDisposable(tuple.Item1);
					cache.TrackDisposable(tuple.Item2);
				}
				cache2 = tuple.Item1;
				cache3 = tuple.Item2;
			}
			_baseRes.Resample(args, profileIndex, mem, cache2);
			DateTime dt = _baseRes.Times[profileIndex];
			int indexOfTime = _altBandedResampler.IndexOfTime(dt);
			if (indexOfTime >= 0)
			{
				_altBandedResampler.Resample(args, indexOfTime, outData, cache3);
			}
			_composeInPlace(mem, outData);
		}
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> outData)
	{
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(points.Length, out mem))
		{
			_baseRes.SamplePoints(points, profileIndex, mem);
			DateTime dt = _baseRes.Times[profileIndex];
			int indexOfTime = _altBandedResampler.IndexOfTime(dt);
			if (indexOfTime >= 0)
			{
				_altBandedResampler.SamplePoints(points, indexOfTime, outData);
			}
			else
			{
				outData.Span.Fill(NoDataValue.GetValueOrDefault());
			}
			_composeInPlace(mem, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.SamplePoints_NaiiveLoop(points, startBandIndex, bandCount, outData);
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}
}
