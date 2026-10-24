using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility;
using Utility.Units;
using Utility.Unsafe;

namespace Geospatial.PairedData;

public class RateTimeSeries : TimeSeries, IAccumulatedTimeSeries, IDiscreteTimeSeries, ITimeSeries
{
	private readonly Versioned<double[]> _accumulatedCache = new Versioned<double[]>();

	public Time RateTimeDenominator { get; set; }

	public IReadOnlyList<double> AccumulatedValues => _accumulatedCache.Get(VersionXY, this, (RateTimeSeries ts) => ts.ComputeAccumulation());

	public RateTimeSeries(Time rateDenominator, ProfileInterpolationMethod method)
	{
		RateTimeDenominator = rateDenominator;
		InterpolationMethod = method;
	}

	public RateTimeSeries(int capacity, Time rateDenominator, ProfileInterpolationMethod method)
		: base(capacity)
	{
		RateTimeDenominator = rateDenominator;
		InterpolationMethod = method;
	}

	public RateTimeSeries(IEnumerable<DateTime> times, IEnumerable<double> values, Time rateDenominator, ProfileInterpolationMethod method)
		: base(times, values)
	{
		RateTimeDenominator = rateDenominator;
		InterpolationMethod = method;
	}

	public RateTimeSeries(IEnumerable<DateTime> times, IEnumerable<float> values, Time rateDenominator, ProfileInterpolationMethod method)
		: base(times, values)
	{
		RateTimeDenominator = rateDenominator;
		InterpolationMethod = method;
	}

	protected RateTimeSeries(bool fakeArg, List<long> dtTicks, List<double> values, Time rateDenominator, ProfileInterpolationMethod method)
		: base(fakeArg, dtTicks, values)
	{
		RateTimeDenominator = rateDenominator;
		InterpolationMethod = method;
	}

	public static RateTimeSeries CreateFromBuffers(List<long> dtTicks, List<double> values, Time rateDenominator, ProfileInterpolationMethod method)
	{
		return new RateTimeSeries(fakeArg: false, dtTicks, values, rateDenominator, method);
	}

	public static RateTimeSeries NoDataTimeSeries(IList<DateTime> times, double noDataValue, Time rateDenominator, ProfileInterpolationMethod method)
	{
		List<long> dtTicks = times.Select((DateTime dt) => dt.Ticks).ToList();
		List<double> values = Enumerable.Repeat(noDataValue, times.Count).ToList();
		return CreateFromBuffers(dtTicks, values, rateDenominator, method);
	}

	public double Slope(DateTime dt)
	{
		long ticks = dt.Ticks;
		if (_dtTicks.Count <= 1 || ticks < _dtTicks.First() || ticks > _dtTicks.Last())
		{
			return -9999.0;
		}
		int binarySearch = MathHelpers.BinarySearch(_dtTicks, ticks);
		long ticks2 = RateTimeDenominator.Ticks;
		long dtTick;
		long dtTick2;
		double computed;
		double computed2;
		if (binarySearch == 0)
		{
			dtTick = _dtTicks[0];
			dtTick2 = _dtTicks[1];
			computed = _values[0];
			computed2 = _values[1];
		}
		else if (binarySearch > 0)
		{
			dtTick = _dtTicks[binarySearch - 1];
			dtTick2 = _dtTicks[binarySearch];
			computed = _values[binarySearch - 1];
			computed2 = _values[binarySearch];
		}
		else
		{
			binarySearch = ~binarySearch;
			dtTick = _dtTicks[binarySearch - 1];
			dtTick2 = _dtTicks[binarySearch];
			computed = _values[binarySearch - 1];
			computed2 = _values[binarySearch];
		}
		long dtTick2AboveDtTick = dtTick2 - dtTick;
		return (computed2 - computed) / ((double)dtTick2AboveDtTick / (double)ticks2);
	}

	public double Integrate(DateTime start, DateTime end)
	{
		long ticks = start.Ticks;
		long ticks2 = end.Ticks;
		if (ticks == ticks2)
		{
			return 0.0;
		}
		bool condition = false;
		if (ticks > ticks2)
		{
			long ticks3 = ticks;
			ticks = ticks2;
			ticks2 = ticks3;
			condition = true;
		}
		long ticks4 = _dtTicks.First();
		long ticks22 = _dtTicks.Last();
		if (_dtTicks.Count <= 1 || ticks2 <= ticks4 || ticks >= ticks22)
		{
			return -9999.0;
		}
		if (ticks < ticks4)
		{
			ticks = ticks4;
		}
		if (ticks2 > ticks22)
		{
			ticks2 = ticks22;
		}
		double integrateInternal = IntegrateInternal(ticks, ticks2);
		if (condition)
		{
			return 0.0 - integrateInternal;
		}
		return integrateInternal;
	}

	public double Integrate()
	{
		return IntegrateInternal(_dtTicks.First(), _dtTicks.Last());
	}

	private double IntegrateInternal(long tickStart, long tickEnd)
	{
		int binarySearchTime = BinarySearchTime(tickStart);
		int binarySearchTime2 = BinarySearchTime(tickEnd);
		int binarySearchTimeStart = ((binarySearchTime < 0) ? (~binarySearchTime) : binarySearchTime);
		int binarySearchTimeEnd = ((binarySearchTime2 < 0) ? (~binarySearchTime2 - 1) : binarySearchTime2);
		double computed = 0.0;
		for (int i = binarySearchTimeStart; i < binarySearchTimeEnd; i++)
		{
			computed += IntegrateSegment(i);
		}
		if (binarySearchTime < 0)
		{
			computed += IntegratePartialPreSegmentInternal(binarySearchTime, tickStart);
		}
		if (binarySearchTime2 < 0)
		{
			computed += IntegratePartialPostSegmentInternal(binarySearchTime2, tickEnd);
		}
		return computed;
	}

	public double IntegrateSegment(int segIdx)
	{
		double ticks = RateTimeDenominator.Ticks;
		if (InterpolationMethod == ProfileInterpolationMethod.ApplyPointToPreviousSegment)
		{
			double dtTickOverTicks = (double)_dtTicks[segIdx] / ticks;
			double dtTickOverTicks2 = (double)_dtTicks[segIdx + 1] / ticks;
			return _values[segIdx + 1] * (dtTickOverTicks2 - dtTickOverTicks);
		}
		if (InterpolationMethod == ProfileInterpolationMethod.Linear)
		{
			double x = (double)_dtTicks[segIdx] / ticks;
			double x2 = (double)_dtTicks[segIdx + 1] / ticks;
			double y = _values[segIdx];
			double y2 = _values[segIdx + 1];
			return Segment.Integrate(x, y, x2, y2);
		}
		throw new NotImplementedException();
	}

	protected double IntegratePartialPreSegmentInternal(int pointIdx, long tickStart)
	{
		double ticks = RateTimeDenominator.Ticks;
		if (InterpolationMethod == ProfileInterpolationMethod.ApplyPointToPreviousSegment)
		{
			double tickStartOverTicks = (double)tickStart / ticks;
			double dtTickOverTicks = (double)_dtTicks[pointIdx] / ticks;
			return _values[pointIdx] * (dtTickOverTicks - tickStartOverTicks);
		}
		if (InterpolationMethod == ProfileInterpolationMethod.Linear)
		{
			double y = QuerySegmentInternal(tickStart, pointIdx);
			double y2 = _values[pointIdx];
			return Segment.Integrate((double)tickStart / ticks, y, (double)_dtTicks[pointIdx] / ticks, y2);
		}
		throw new NotImplementedException();
	}

	protected double IntegratePartialPostSegmentInternal(int pointIdx, long tickEnd)
	{
		double ticks = RateTimeDenominator.Ticks;
		if (InterpolationMethod == ProfileInterpolationMethod.ApplyPointToPreviousSegment)
		{
			double dtTickOverTicks = (double)_dtTicks[pointIdx] / ticks;
			double tickEndOverTicks = (double)tickEnd / ticks;
			return _values[pointIdx + 1] * (tickEndOverTicks - dtTickOverTicks);
		}
		if (InterpolationMethod == ProfileInterpolationMethod.Linear)
		{
			double y = _values[pointIdx];
			double y2 = QuerySegmentInternal(tickEnd, pointIdx + 1);
			return Segment.Integrate((double)_dtTicks[pointIdx] / ticks, y, (double)tickEnd / ticks, y2);
		}
		throw new NotImplementedException();
	}

	private double[] ComputeAccumulation()
	{
		double[] accumulation = new double[Count];
		if (Count == 0)
		{
			return accumulation;
		}
		accumulation[0] = 0.0;
		double accumulationIntegrateSegmentTerm = 0.0;
		for (int i = 1; i < Count; i++)
		{
			accumulationIntegrateSegmentTerm = (accumulation[i] = accumulationIntegrateSegmentTerm + IntegrateSegment(i - 1));
		}
		return accumulation;
	}

	public double SampleAccumulated(DateTime dt)
	{
		return CumulativeTimeSeriesUnsafe().Sample(dt);
	}

	private TimeSeries CumulativeTimeSeriesUnsafe()
	{
		return TimeSeries.CreateFromBuffers(_dtTicks, UnsafeHelpers.NewListFromBuffer(AccumulatedValues as double[]));
	}

	public DateTime TimeToAccumulate(double accumulatedYValue, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		return TimeToAccumulate_Internal(accumulatedYValue, AccumulatedValues, tiebreaker);
	}

	protected override long InterpolateSegmentTime(IReadOnlyList<double> accumulatedValues, int segIdx, double yValue)
	{
		if (InterpolationMethod == ProfileInterpolationMethod.ApplyPointToPreviousSegment)
		{
			return base.InterpolateSegmentTime(accumulatedValues, segIdx, yValue);
		}
		if (InterpolationMethod == ProfileInterpolationMethod.Linear)
		{
			int index = segIdx + 1;
			long dtTick = _dtTicks[segIdx];
			long dtTick2 = _dtTicks[index];
			double t = (double)dtTick / (double)RateTimeDenominator.Ticks;
			double t2 = (double)dtTick2 / (double)RateTimeDenominator.Ticks;
			TimeSeriesPoint aPt = new TimeSeriesPoint(t, accumulatedValues[segIdx], _values[segIdx]);
			TimeSeriesPoint bPt = new TimeSeriesPoint(t2, accumulatedValues[index], _values[index]);
			new TimeSeriesSegment(aPt, bPt).TimeAtPosition(yValue, out var time, out var time2);
			long timeTimesTicks = (long)(time * (double)RateTimeDenominator.Ticks);
			long time2TimesTicks = (long)(time2 * (double)RateTimeDenominator.Ticks);
			if (timeTimesTicks >= dtTick && timeTimesTicks <= dtTick2)
			{
				return timeTimesTicks;
			}
			if (time2TimesTicks >= dtTick && time2TimesTicks <= dtTick2)
			{
				return time2TimesTicks;
			}
			return 0L;
		}
		throw new NotSupportedException();
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

	public TimeSpan ComputeStandardDeviation()
	{
		DateTime dateTime = this.CenterOfMass();
		if (dateTime == StartTime)
		{
			return new TimeSpan(0L);
		}
		TimeSpan timeSpan = TimeSpan.FromHours((EndTime - StartTime).TotalHours / 1000.0);
		double computed = 0.0;
		for (int i = 0; i < 999; i++)
		{
			DateTime dateTime2 = StartTime + TimeSpan.FromHours(timeSpan.TotalHours * (double)i);
			DateTime end = dateTime2 + timeSpan;
			double sampleAccumulatedDuring = SampleAccumulatedDuring(dateTime2, end);
			DateTime dateTime3 = dateTime2 + TimeSpan.FromHours(timeSpan.TotalHours / 2.0);
			TimeSpan timeSpan2 = dateTime - dateTime3;
			computed += sampleAccumulatedDuring * (timeSpan2.TotalHours * timeSpan2.TotalHours);
		}
		return TimeSpan.FromHours(Math.Sqrt(computed / 1000.0));
	}

	public void ComputeStandardDeviation(out TimeSpan leftStdDev, out TimeSpan rightStdDev)
	{
		DateTime dateTime = this.CenterOfMass();
		if (dateTime == StartTime)
		{
			leftStdDev = new TimeSpan(0L);
			rightStdDev = new TimeSpan(0L);
			return;
		}
		TimeSpan timeSpan = TimeSpan.FromHours((EndTime - StartTime).TotalHours / 1000.0);
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = 0; i < 999; i++)
		{
			DateTime dateTime2 = StartTime + TimeSpan.FromHours(timeSpan.TotalHours * (double)i);
			DateTime end = dateTime2 + timeSpan;
			double sampleAccumulatedDuring = SampleAccumulatedDuring(dateTime2, end);
			DateTime dateTime3 = dateTime2 + TimeSpan.FromHours(timeSpan.TotalHours / 2.0);
			TimeSpan timeSpan2 = dateTime - dateTime3;
			if (timeSpan2.TotalHours > 0.0)
			{
				computed += sampleAccumulatedDuring * (timeSpan2.TotalHours * timeSpan2.TotalHours);
			}
			else
			{
				computed2 += sampleAccumulatedDuring * (timeSpan2.TotalHours * timeSpan2.TotalHours);
			}
		}
		double d = computed / 500.0;
		double d2 = computed2 / 500.0;
		double computed3 = Math.Sqrt(d);
		double sqrtD2 = Math.Sqrt(d2);
		leftStdDev = TimeSpan.FromHours(computed3);
		rightStdDev = TimeSpan.FromHours(sqrtD2);
	}

	public void ShiftCenterOfMassTo(DateTime dt)
	{
		Shift(dt - this.CenterOfMass());
	}

	public PeriodCumulativeTimeSeries ConvertToPeriodCumulative(TimeSpan? forceFirstTimestep = null)
	{
		PeriodCumulativeTimeSeries periodCumulativeTimeSeries = new PeriodCumulativeTimeSeries(Count);
		if (Count == 0)
		{
			return periodCumulativeTimeSeries;
		}
		double ticks = RateTimeDenominator.Ticks;
		if (!forceFirstTimestep.HasValue || !forceFirstTimestep.HasValue)
		{
			periodCumulativeTimeSeries.Add(_dtTicks[0], 0.0);
		}
		else
		{
			periodCumulativeTimeSeries.Add(_dtTicks[0], _values[0] * ((double)forceFirstTimestep.Value.Ticks / ticks));
		}
		for (int i = 1; i < Count; i++)
		{
			long dtTick = _dtTicks[i - 1];
			double dtTicksDtTickTerm = (double)(_dtTicks[i] - dtTick) / ticks;
			double computed = _values[i];
			periodCumulativeTimeSeries.Add(_dtTicks[i], computed * dtTicksDtTickTerm);
		}
		return periodCumulativeTimeSeries;
	}

	public PeriodAverageTimeSeries ResampleToPeriodAverage(IReadOnlyList<DateTime> newTimes, Time rateTimeDenominator)
	{
		if (newTimes == null)
		{
			throw new ArgumentNullException("newTimes");
		}
		if (newTimes.Count == 0)
		{
			throw new ArgumentException("No times provided");
		}
		TimeSeries timeSeries = CumulativeTimeSeriesUnsafe();
		List<double> doubleList = new List<double>(newTimes.Count);
		double sample = Sample(newTimes.First());
		if (sample == -9999.0)
		{
			doubleList.Add(0.0);
		}
		else
		{
			doubleList.Add(sample);
		}
		double sample2 = 0.0;
		for (int i = 1; i < newTimes.Count; i++)
		{
			double sample3 = timeSeries.Sample(newTimes[i]);
			if (sample2 == -9999.0 || sample3 == -9999.0)
			{
				double sample4 = Sample(newTimes[i]);
				if (sample4 == -9999.0)
				{
					doubleList.Add(0.0);
				}
				else
				{
					doubleList.Add(sample4);
				}
			}
			else
			{
				double sample3AboveSample2 = sample3 - sample2;
				sample3AboveSample2 = Math.Round(sample3AboveSample2, 10);
				_ = 0.0;
				TimeSpan timeSpan = newTimes[i] - newTimes[i - 1];
				long ticks = timeSpan.Ticks - timeSpan.Ticks % 100;
				double fromTimespan = rateTimeDenominator.FromTimespan(new TimeSpan(ticks));
				if (fromTimespan == 0.0)
				{
					doubleList.Add(doubleList.Last());
				}
				else
				{
					doubleList.Add(sample3AboveSample2 / fromTimespan);
				}
			}
			sample2 = sample3;
		}
		return new PeriodAverageTimeSeries(newTimes, doubleList, rateTimeDenominator);
	}

	public RateTimeSeries ResampleToPeriodAverage(IReadOnlyList<DateTime> newTimes)
	{
		return ResampleToPeriodAverage(newTimes, RateTimeDenominator);
	}

	public void ConvertRate(Time newTimeUnits)
	{
		double ticks = RateTimeDenominator.Ticks;
		double ticksOverTicks = (double)newTimeUnits.Ticks / ticks;
		for (int i = 0; i < Count; i++)
		{
			_values[i] *= ticksOverTicks;
		}
		RateTimeDenominator = newTimeUnits;
	}

	public new RateTimeSeries Clone()
	{
		return new RateTimeSeries(Times, Values, RateTimeDenominator, InterpolationMethod);
	}

	IAccumulatedTimeSeries IAccumulatedTimeSeries.Clone()
	{
		return Clone();
	}

	public double SampleRate(DateTime dt, Time rateTimeDenominator)
	{
		double sample = Sample(dt);
		if (rateTimeDenominator == RateTimeDenominator)
		{
			return sample;
		}
		return RateTimeDenominator.ConvertTo(rateTimeDenominator, sample);
	}
}
