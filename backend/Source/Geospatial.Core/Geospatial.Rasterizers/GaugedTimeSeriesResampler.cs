using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Extensions;

namespace Geospatial.Rasterizers;

public abstract class GaugedTimeSeriesResampler : ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps, IGauged
{
	private ISpatialInterpolator _spatialInterpolator;

	public List<TimeSeriesGauge> Gauges { get; protected set; }

	public int GaugeCount => Gauges.Count;

	public ISpatialInterpolator SpatialInterpolator
	{
		get
		{
			return _spatialInterpolator;
		}
		protected set
		{
			if (value == null)
			{
				throw new ArgumentNullException();
			}
			_spatialInterpolator = value;
			_spatialInterpolator.Initialize(Gauges.Select((TimeSeriesGauge g) => g.Location).ToList());
		}
	}

	public TimeSpan TimeSpanFilterTolerance { get; private set; } = TimeSpan.FromHours(1E-05);

	public int RoundSamplesTo { get; set; } = 3;

	public DateTime StartTime => Times.FirstOrDefault();

	public DateTime EndTime => Times.LastOrDefault();

	public IReadOnlyList<DateTime> Times { get; protected set; }

	public Extent ValidExtent { get; protected set; }

	public float? NoDataValue => -9999f;

	public bool CanInterpolateTimesteps => true;

	public int ProfileCount => Times?.Count ?? 0;

	public GaugedTimeSeriesResampler(IList<TimeSeriesGauge> gauges, ISpatialInterpolator interpolator)
	{
		gauges.ThrowIfNullOrEmpty("gauges");
		if (interpolator == null)
		{
			throw new ArgumentNullException("interpolator");
		}
		Gauges = gauges.ToList();
		Times = (from dt in Gauges.SelectMany((TimeSeriesGauge g) => g.TimeSeries.Times)
			orderby dt
			select dt).Distinct().ToList();
		SpatialInterpolator = interpolator;
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		ResampleInternal(args.Raster, time, outData);
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<float> outData, ResampleCache cache = null)
	{
		Resample(args, Times[profileIndex], outData, cache);
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<float>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	protected virtual void ResampleInternal(RasterDefinition raster, DateTime time, Memory<float> outData)
	{
		Span<float> outDataSpan = outData.Span;
		for (int i = 0; i < raster.CellCount; i++)
		{
			outDataSpan[i] = (float)SamplePointTimeSeries(raster.CellCenter(i)).Sample(time);
		}
	}

	public abstract TimeSeries SamplePointTimeSeries(Point pt);

	protected TimeSeries NoDataTimeSeries()
	{
		return new TimeSeries(Times, Enumerable.Repeat(-9999.0, ProfileCount));
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] outData)
	{
		int length = points.Length;
		Span<Point> pointsSpan = points.Span;
		List<TimeSeries> timeSeries = new List<TimeSeries>(length);
		for (int i = 0; i < length; i++)
		{
			timeSeries.Add(SamplePointTimeSeries(pointsSpan[i]));
		}
		for (int j = 0; j < bandCount; j++)
		{
			Span<float> outDataSpan = outData[j].Span;
			DateTime dt = Times[j + bandStart];
			for (int k = 0; k < length; k++)
			{
				outDataSpan[k] = (float)timeSeries[k].Sample(dt);
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		SamplePoints(points, bandIndex, 1, new Memory<float>[1] { outData });
	}

	private Polyline DiscretizeForSampling(Polyline input)
	{
		double length = input.Length;
		return input.DiscretizeLine(length / 100.0);
	}
}
