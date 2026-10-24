using System;
using System.Collections.Generic;
using Utility;
using Utility.Units;

namespace Geospatial.PairedData;

public class PeriodCumulativeTimeSeries : TimeSeries, IAccumulatedTimeSeries, IDiscreteTimeSeries, ITimeSeries
{
	private readonly Versioned<double[]> _accumulatedCache = new Versioned<double[]>();

	public IReadOnlyList<double> AccumulatedValues => AccumulatedValuesInternal;

	private double[] AccumulatedValuesInternal => _accumulatedCache.Get(VersionXY, this, (PeriodCumulativeTimeSeries ts) => ts.ComputeSummation());

	public PeriodCumulativeTimeSeries()
	{
		Init();
	}

	public PeriodCumulativeTimeSeries(int capacity)
		: base(capacity)
	{
		Init();
	}

	public PeriodCumulativeTimeSeries(IEnumerable<DateTime> times, IEnumerable<double> values)
		: base(times, values)
	{
		Init();
	}

	private void Init()
	{
	}

	public void ShiftCenterOfMassTo(DateTime dt)
	{
		Shift(this.CenterOfMass() - dt);
	}

	public TimeSpan ComputeStandardDeviation()
	{
		throw new NotImplementedException();
	}

	public void ComputeStandardDeviation(out TimeSpan leftStdDev, out TimeSpan rightStdDev)
	{
		throw new NotImplementedException();
	}

	public double SampleRate(DateTime dt, Time rateTimeDenominator)
	{
		throw new NotImplementedException();
	}

	public RateTimeSeries ToPeriodAverage(Time rateTimeDenominator)
	{
		List<double> applyPointToPreviousSegmentIds = new List<double>(Count);
		applyPointToPreviousSegmentIds.Add(0.0);
		double ticks = rateTimeDenominator.Ticks;
		for (int i = 1; i < Count; i++)
		{
			DateTime dateTime = Times[i - 1];
			DateTime dateTime2 = Times[i];
			double computed = Values[i];
			if (computed == -9999.0)
			{
				applyPointToPreviousSegmentIds.Add(-9999.0);
				continue;
			}
			double ticksOverTicks = (double)(dateTime2 - dateTime).Ticks / ticks;
			applyPointToPreviousSegmentIds.Add(computed / ticksOverTicks);
		}
		return new RateTimeSeries(Times, applyPointToPreviousSegmentIds, rateTimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
	}

	public PeriodCumulativeTimeSeries Resample(IList<DateTime> newTimes)
	{
		if (newTimes == null)
		{
			throw new ArgumentNullException("newTimes");
		}
		List<double> doubleList = new List<double>(newTimes.Count);
		double sampleTerm = Values[0];
		doubleList.Add(0.0);
		Span<double> sourceValues = AccumulatedValuesInternal.AsSpan();
		Span<long> timesUnsafe = GetTimesUnsafe().Span;
		for (int i = 1; i < newTimes.Count; i++)
		{
			double sample = TimeSeries.Sample(timesUnsafe, sourceValues, newTimes[i].Ticks, ExtrapolationBehavior.ReturnNoData);
			if (sampleTerm == -9999.0 || sample == -9999.0)
			{
				doubleList.Add(sample);
			}
			else
			{
				doubleList.Add(sample - sampleTerm);
			}
			sampleTerm = sample;
		}
		return new PeriodCumulativeTimeSeries(newTimes, doubleList);
	}

	public PeriodAverageTimeSeries ResampleToPeriodAverage(IReadOnlyList<DateTime> newTimes, Time rateTimeDenominator)
	{
		throw new NotImplementedException();
	}

	public new PeriodCumulativeTimeSeries Clone()
	{
		return new PeriodCumulativeTimeSeries(Times, Values);
	}

	IAccumulatedTimeSeries IAccumulatedTimeSeries.Clone()
	{
		return Clone();
	}

	public double SampleAccumulated(DateTime time)
	{
		double[] accumulatedValuesInternal = AccumulatedValuesInternal;
		return TimeSeries.Sample(GetTimesUnsafe().Span, accumulatedValuesInternal.AsSpan(), time.Ticks, ExtrapolationBehavior.ReturnNoData);
	}

	public double SampleAccumulatedDuring(DateTime start, DateTime end)
	{
		if (start >= EndTime || end <= StartTime)
		{
			return -9999.0;
		}
		if (start < StartTime)
		{
			start = StartTime;
		}
		if (end > EndTime)
		{
			end = EndTime;
		}
		return SampleAccumulated(end) - SampleAccumulated(start);
	}

	public DateTime TimeToAccumulate(double accumulatedYValue, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		return TimeToAccumulate_Internal(accumulatedYValue, AccumulatedValues, tiebreaker);
	}
}
