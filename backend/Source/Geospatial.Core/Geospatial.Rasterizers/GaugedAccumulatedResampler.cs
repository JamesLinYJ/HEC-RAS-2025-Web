using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility;
using Utility.Units;

namespace Geospatial.Rasterizers;

public abstract class GaugedAccumulatedResampler : GaugedTimeSeriesResampler, IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	public new IList<AccumulatedGauge> Gauges { get; }

	public Time TimeDenominator => Time.Hours;

	public bool HasDiscreteTimesteps => true;

	public TimeSeriesDataType NativeDataType => TimeSeriesDataType.Rate;

	public GaugedAccumulatedResampler(IList<AccumulatedGauge> gauges, ISpatialInterpolator interpolator)
		: base(gauges?.Select((Func<AccumulatedGauge, TimeSeriesGauge>)((AccumulatedGauge g) => g)).ToList(), interpolator)
	{
		Gauges = gauges.ToList();
	}

	public void ResampleAccumulated(ResampleArgs args, DateTime start, DateTime end, Memory<float> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		RasterDefinition rd = args.Raster;
		Parallel.SmartFor(rd.CellCount, (int startIdx, int endIdx) =>
		{
			Span<float> outDataSpan = outData.Span;
			for (int i = startIdx; i <= endIdx; i++)
			{
				outDataSpan[i] = (float)SampleRateTimeSeries(rd.CellCenter(i)).SampleAccumulatedDuring(start, end);
			}
		});
	}

	protected override void ResampleInternal(RasterDefinition raster, DateTime time, Memory<float> outData)
	{
		Parallel.SmartFor(raster.CellCount, (int start, int end) =>
		{
			Span<float> outDataSpan = outData.Span;
			for (int i = start; i <= end; i++)
			{
				outDataSpan[i] = (float)SampleRateTimeSeries(raster.CellCenter(i)).SampleRate(time, TimeDenominator);
			}
		});
	}

	public void ResampleRates(ResampleArgs args, DateTime time, Memory<float> outData)
	{
		Resample(args, time, outData);
	}

	public abstract RateTimeSeries SampleRateTimeSeries(Point pt);

	public override TimeSeries SamplePointTimeSeries(Point pt)
	{
		return SampleRateTimeSeries(pt);
	}

	protected new RateTimeSeries NoDataTimeSeries()
	{
		return new RateTimeSeries(Times, Enumerable.Repeat(-9999.0, ProfileCount), TimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
	}
}
