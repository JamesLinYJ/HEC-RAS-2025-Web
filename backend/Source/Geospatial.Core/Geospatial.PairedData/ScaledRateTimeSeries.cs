using System;
using System.Collections.Generic;
using Utility.DataStructures;
using Utility.Units;

namespace Geospatial.PairedData;

public class ScaledRateTimeSeries : IAccumulatedTimeSeries, IDiscreteTimeSeries, ITimeSeries
{
	private double _ratio;

	private IAccumulatedTimeSeries _baseTS;

	public IReadOnlyList<double> AccumulatedValues { get; }

	public IReadOnlyList<DateTime> Times => _baseTS.Times;

	public IReadOnlyList<double> Values { get; }

	public TimeSeriesDataBehavior Behavior => _baseTS.Behavior;

	public DateTime StartTime => _baseTS.StartTime;

	public DateTime EndTime => _baseTS.EndTime;

	public ScaledRateTimeSeries(IAccumulatedTimeSeries baseTS, double ratio)
	{
		_baseTS = baseTS;
		_ratio = ratio;
		Values = new TranslatedList<double, double>(_baseTS.Values, (double d) => d * _ratio);
	}

	public IAccumulatedTimeSeries Clone()
	{
		return new ScaledRateTimeSeries(_baseTS, _ratio);
	}

	public TimeSpan ComputeStandardDeviation()
	{
		return TimeSpan.FromHours(_baseTS.ComputeStandardDeviation().TotalHours * Math.Sqrt(_ratio));
	}

	public void ComputeStandardDeviation(out TimeSpan leftStdDev, out TimeSpan rightStdDev)
	{
		_baseTS.ComputeStandardDeviation(out leftStdDev, out rightStdDev);
		leftStdDev = TimeSpan.FromHours(leftStdDev.TotalHours * Math.Sqrt(_ratio));
		rightStdDev = TimeSpan.FromHours(rightStdDev.TotalHours * Math.Sqrt(_ratio));
	}

	public PeriodAverageTimeSeries ResampleToPeriodAverage(IReadOnlyList<DateTime> newTimes, Time rateTimeDenominator)
	{
		throw new NotSupportedException();
	}

	public double Sample(DateTime time)
	{
		return _baseTS.Sample(time) * _ratio;
	}

	public double SampleAccumulated(DateTime dt)
	{
		return _baseTS.SampleAccumulated(dt) * _ratio;
	}

	public double SampleAccumulatedDuring(DateTime start, DateTime end)
	{
		return _baseTS.SampleAccumulatedDuring(start, end) * _ratio;
	}

	public double SampleRate(DateTime dt, Time rateTimeDenominator)
	{
		return _baseTS.SampleRate(dt, rateTimeDenominator) * _ratio;
	}

	public void Scale(double scaleFactor)
	{
		_ratio *= scaleFactor;
	}

	public void Shift(TimeSpan deltaTime)
	{
		throw new NotSupportedException();
	}

	public DateTime TimeToAccumulate(double accumulatedYValue, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		return _baseTS.TimeToAccumulate(accumulatedYValue, tiebreaker);
	}

	public double[] Sample(IList<DateTime> times)
	{
		double[] doubleValues = new double[times.Count];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			doubleValues[i] = Sample(times[i]);
		}
		return doubleValues;
	}
}
