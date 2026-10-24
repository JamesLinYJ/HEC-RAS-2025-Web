using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Units;

namespace Geospatial.PairedData;

public class CumulativeTimeSeries : TimeSeries, IAccumulatedTimeSeries, IDiscreteTimeSeries, ITimeSeries
{
	public IReadOnlyList<double> AccumulatedValues => Values;

	public static CumulativeTimeSeries SanitizeAndBuild(IEnumerable<DateTime> times, IEnumerable<double> values)
	{
		if (times == null || values == null || times.Count() != values.Count())
		{
			throw new ArgumentException("Bad Times/Values arguments");
		}
		List<double> doubleList = values.ToList();
		double computed = 0.0;
		double computed2 = doubleList[0];
		for (int i = 1; i < doubleList.Count; i++)
		{
			double computed3 = doubleList[i];
			if (computed3 != -9999.0)
			{
				if (computed3 + computed > computed2)
				{
					doubleList[i] += computed;
					computed2 = computed3 + computed;
				}
				else
				{
					computed += computed3;
					doubleList[i] = computed2 + computed;
					computed2 += computed;
				}
			}
		}
		return new CumulativeTimeSeries(times, doubleList);
	}

	public CumulativeTimeSeries()
	{
	}

	public CumulativeTimeSeries(int capacity)
		: base(capacity)
	{
	}

	public CumulativeTimeSeries(IEnumerable<DateTime> times, IEnumerable<double> values)
		: base(times, values)
	{
	}

	private void ValidateData()
	{
		if (Count == 0)
		{
			return;
		}
		DateTime dateTime = DateTime.MinValue;
		double minValue = double.MinValue;
		for (int i = 0; i < Count; i++)
		{
			DateTime dateTime2 = Times[i];
			double minValue2 = Values[i];
			if (dateTime2 <= dateTime)
			{
				throw new Exception("Invalid Times Array");
			}
			if (minValue2 != -9999.0 && minValue != -9999.0 && minValue2 < minValue)
			{
				throw new Exception("Invalid Values Array");
			}
			dateTime = dateTime2;
			minValue = minValue2;
		}
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
			double computed = Values[i - 1];
			double computed2 = Values[i];
			if (computed2 == -9999.0)
			{
				applyPointToPreviousSegmentIds.Add(-9999.0);
				continue;
			}
			double ticksOverTicks = (double)(dateTime2 - dateTime).Ticks / ticks;
			applyPointToPreviousSegmentIds.Add((computed2 - computed) / ticksOverTicks);
		}
		return new RateTimeSeries(Times, applyPointToPreviousSegmentIds, rateTimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
	}

	public PeriodCumulativeTimeSeries Resample(IReadOnlyList<DateTime> newTimes)
	{
		if (newTimes == null)
		{
			throw new ArgumentNullException("newTimes");
		}
		List<double> doubleList = new List<double>(newTimes.Count);
		double sampleAccumulatedTerm = Values[0];
		doubleList.Add(0.0);
		for (int i = 1; i < newTimes.Count; i++)
		{
			double sampleAccumulated = SampleAccumulated(newTimes[i]);
			if (sampleAccumulatedTerm == -9999.0 || sampleAccumulated == -9999.0)
			{
				doubleList.Add(sampleAccumulated);
			}
			else
			{
				doubleList.Add(sampleAccumulated - sampleAccumulatedTerm);
			}
			sampleAccumulatedTerm = sampleAccumulated;
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
		return Sample(time);
	}

	public double SampleAccumulatedDuring(DateTime start, DateTime end)
	{
		return SampleAccumulated(end) - SampleAccumulated(start);
	}

	public DateTime TimeToAccumulate(double accumulatedYValue, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		return TimeToAccumulate_Internal(accumulatedYValue, AccumulatedValues, tiebreaker);
	}
}
