using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Units;

namespace Geospatial.Rasters;

internal class AccumulatedResamplerValueTransformed : IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private readonly IAccumulatedResampler<float> _resampler;

	private readonly ConverterDelegates.ConvertInPlaceNDV<float> _transformer;

	public bool HasDiscreteTimesteps => _resampler.HasDiscreteTimesteps;

	public TimeSeriesDataType NativeDataType => _resampler.NativeDataType;

	public Extent ValidExtent => _resampler.ValidExtent;

	public float? NoDataValue => _resampler.NoDataValue;

	public IReadOnlyList<DateTime> Times => _resampler.Times;

	public Time TimeDenominator { get; }

	public AccumulatedResamplerValueTransformed(IAccumulatedResampler<float> resampler, ConverterDelegates.ConvertInPlaceNDV<float> transformer, Time newTimeUnits)
	{
		_resampler = resampler;
		_transformer = transformer;
		TimeDenominator = newTimeUnits;
	}

	public void ResampleAccumulated(ResampleArgs args, DateTime start, DateTime end, Memory<float> outData)
	{
		_resampler.ResampleAccumulated(args, start, end, outData);
		_transformer(outData, NoDataValue);
	}

	public void ResampleRates(ResampleArgs args, DateTime time, Memory<float> outData)
	{
		_resampler.ResampleRates(args, time, outData);
		_transformer(outData, NoDataValue);
	}

	public RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		RateTimeSeries rateTimeSeries = _resampler.SampleRateTimeSeries(pt);
		float[] floatValues = rateTimeSeries.Values.Select((double d) => (float)d).ToArray();
		_transformer(floatValues, NoDataValue);
		return new RateTimeSeries(rateTimeSeries.Times, ((IEnumerable<float>)floatValues).Select((Func<float, double>)((float f) => f)).ToList(), rateTimeSeries.RateTimeDenominator, rateTimeSeries.InterpolationMethod);
	}
}
