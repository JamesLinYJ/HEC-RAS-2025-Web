using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rasters;

public class MergedTimeSeriesResampler<T> : ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
	private readonly List<DateTime> _times;

	protected readonly Tuple<ITimeSeriesResampler<T>, DateTime, int>[] _timestepMap;

	public bool CanInterpolateTimesteps => false;

	public int ProfileCount => Times.Count;

	public Extent ValidExtent { get; }

	public T? NoDataValue { get; }

	public IReadOnlyList<DateTime> Times => _times;

	public MergedTimeSeriesResampler(IList<ITimeSeriesResampler<T>> inputs)
	{
		if (inputs == null)
		{
			throw new ArgumentNullException();
		}
		if (inputs.Count == 0)
		{
			return;
		}
		ValidExtent = Extent.Union(inputs.Select((ITimeSeriesResampler<T> i) => i.ValidExtent));
		NoDataValue = inputs.First().NoDataValue;
		List<Tuple<ITimeSeriesResampler<T>, DateTime, int>> tupleList = new List<Tuple<ITimeSeriesResampler<T>, DateTime, int>>();
		for (int intervalIndex = 0; intervalIndex < inputs.Count; intervalIndex++)
		{
			ITimeSeriesResampler<T> timeSeriesResampler = inputs[intervalIndex];
			for (int intervalIndex2 = 0; intervalIndex2 < timeSeriesResampler.ProfileCount; intervalIndex2++)
			{
				tupleList.Add(new Tuple<ITimeSeriesResampler<T>, DateTime, int>(timeSeriesResampler, timeSeriesResampler.Times[intervalIndex2], intervalIndex2));
			}
		}
		_timestepMap = tupleList.OrderBy((Tuple<ITimeSeriesResampler<T>, DateTime, int> tup) => tup.Item2).ToArray();
		_times = _timestepMap.Select((Tuple<ITimeSeriesResampler<T>, DateTime, int> tup) => tup.Item2).ToList();
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		int binarySearch = MathHelpers.BinarySearch(Times, time);
		if (binarySearch < 0)
		{
			throw new ArgumentException("No timestep interpolation allowed.");
		}
		Resample(args, binarySearch, outData);
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		Tuple<ITimeSeriesResampler<T>, DateTime, int> tuple = _timestepMap[profileIndex];
		tuple.Item1.Resample(args, tuple.Item3, outData);
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		throw new NotImplementedException();
	}
}
