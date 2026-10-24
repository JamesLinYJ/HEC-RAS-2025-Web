using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;
using Utility.Units;

namespace Geospatial.Rasters;

internal class TimeSeriesAccumulatorF : IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private readonly ITimeSeriesResampler<float> _ds;

	private readonly TimeSeriesDataType _dt;

	private readonly TimestepCollection _tsm;

	public ProfileInterpolationMethod Interp { get; set; } = ProfileInterpolationMethod.ApplyPointToPreviousSegment;

	public Extent ValidExtent => _ds.ValidExtent;

	public float? NoDataValue => _ds.NoDataValue;

	public IReadOnlyList<DateTime> Times => _ds.Times;

	public bool HasDiscreteTimesteps => true;

	public TimeSeriesDataType NativeDataType => _dt;

	public Time TimeDenominator { get; }

	public TimeSeriesAccumulatorF(ITimeSeriesResampler<float> ds, TimeSeriesDataType dataType, Time timeUnits)
	{
		_ds = ds;
		_dt = dataType;
		_tsm = new TimestepCollection(ds.Times, dataType);
		TimeDenominator = timeUnits;
	}

	public TimeSeriesAccumulatorF(ITimeSeriesResampler<float> ds, TimeSeriesDataType dataType, Time timeUnits, ProfileInterpolationMethod interp)
	{
		_ds = ds;
		_dt = dataType;
		Interp = interp;
		_tsm = new TimestepCollection(ds.Times, dataType);
		_tsm.RateTimeDenominator = timeUnits;
		_tsm.InterpolationMethod = interp;
		TimeDenominator = timeUnits;
	}

	public void ResampleAccumulated(ResampleArgs args, DateTime start, DateTime end, Memory<float> outData)
	{
		args.ThrowIfBadSize(outData);
		List<TsWeight> accumulationWeights = _tsm.GetAccumulationWeights(start, end);
		if (accumulationWeights.Count == 0)
		{
			outData.Span.Fill(NoDataValue.GetValueOrDefault());
			return;
		}
		outData.Span.Fill(0f);
		int length = outData.Length;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(length);
		Memory<float> timestepMem = memoryOwner.Memory.Slice(0, length);
		float ndv = _ds.NoDataValue.GetValueOrDefault();
		for (int i = 0; i < accumulationWeights.Count; i++)
		{
			TsWeight tsWeight = accumulationWeights[i];
			_ds.Resample(args, tsWeight.Index, timestepMem);
			float ratio = (float)tsWeight.Weight;
			Parallel.SmartFor(length, (int ordinal, int count) =>
			{
				Span<float> outDataSpan = outData.Span;
				Span<float> timestepMemSpan = timestepMem.Span;
				for (int j = ordinal; j <= count; j++)
				{
					float timestepMem2 = timestepMemSpan[j];
					if (timestepMem2 != ndv)
					{
						outDataSpan[j] += timestepMem2 * ratio;
					}
				}
			});
		}
	}

	public void ResampleRates(ResampleArgs args, DateTime time, Memory<float> outData)
	{
		_tsm.GetRateWeights(time, out var weight, out var weight2);
		if (weight.IsNull() && weight2.IsNull())
		{
			outData.Span.Fill(NoDataValue.GetValueOrDefault());
			return;
		}
		if (!weight.IsNull() && weight2.IsNull())
		{
			_ds.Resample(args, weight.Index, outData);
			if (NoDataValue.HasValue)
			{
				Buffers.MultiplyConstant(outData.Span, (float)weight.Weight, NoDataValue.Value);
			}
			else
			{
				Buffers.MultiplyConstant(outData.Span, (float)weight.Weight);
			}
			return;
		}
		if (weight.IsNull() && !weight2.IsNull())
		{
			_ds.Resample(args, weight2.Index, outData);
			if (NoDataValue.HasValue)
			{
				Buffers.MultiplyConstant(outData.Span, (float)weight2.Weight, NoDataValue.Value);
			}
			else
			{
				Buffers.MultiplyConstant(outData.Span, (float)weight2.Weight);
			}
			return;
		}
		_ds.Resample(args, weight.Index, outData);
		int length = outData.Length;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(length);
		Memory<float> mem = memoryOwner.Memory.Slice(0, length);
		_ds.Resample(args, weight2.Index, mem);
		float ratio1 = (float)weight.Weight;
		float ratio2 = (float)weight2.Weight;
		float ndv = NoDataValue.GetValueOrDefault();
		Parallel.SmartFor(length, (int start, int end) =>
		{
			Span<float> outDataSpan = outData.Span;
			Span<float> memSpan = mem.Span;
			for (int i = start; i <= end; i++)
			{
				float outData2 = outDataSpan[i];
				float mem2 = memSpan[i];
				if (outData2 == ndv && mem2 == ndv)
				{
					outDataSpan[i] = ndv;
				}
				else if (outData2 == ndv && mem2 != ndv)
				{
					outDataSpan[i] = mem2 * ratio2;
				}
				else if (outData2 != ndv && mem2 == ndv)
				{
					outDataSpan[i] = outData2 * ratio1;
				}
				else
				{
					outDataSpan[i] = outData2 * ratio1 + mem2 * ratio2;
				}
			}
		});
	}

	public RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		TimeSeries timeSeries = _ds.SamplePointTimeSeries(pt);
		if (_dt == TimeSeriesDataType.Amount)
		{
			return new CumulativeTimeSeries(timeSeries.Times, timeSeries.Values).ToPeriodAverage(TimeDenominator);
		}
		if (_dt == TimeSeriesDataType.Delta)
		{
			return new PeriodCumulativeTimeSeries(timeSeries.Times, timeSeries.Values).ToPeriodAverage(TimeDenominator);
		}
		if (_dt == TimeSeriesDataType.Rate)
		{
			return new RateTimeSeries(timeSeries.Times, timeSeries.Values, TimeDenominator, Interp);
		}
		throw new NotImplementedException();
	}
}
