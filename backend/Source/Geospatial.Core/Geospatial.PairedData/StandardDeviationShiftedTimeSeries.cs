using System;
using System.Collections.Generic;
using Utility.Units;

namespace Geospatial.PairedData;

public class StandardDeviationShiftedTimeSeries : IAccumulatedTimeSeries, IDiscreteTimeSeries, ITimeSeries
{
	private IAccumulatedTimeSeries _baseTimeSeries;

	private DateTime _com;

	private TimeSpan _newStdDev;

	private TimeSpan _origStdDev;

	public IReadOnlyList<DateTime> Times { get; }

	public IReadOnlyList<double> Values => _baseTimeSeries.Values;

	public IReadOnlyList<double> AccumulatedValues
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public DateTime StartTime
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public DateTime EndTime
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public TimeSeriesDataBehavior Behavior
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public StandardDeviationShiftedTimeSeries(IAccumulatedTimeSeries baseTimeSeries, TimeSpan newStandardDeviation)
	{
		_baseTimeSeries = baseTimeSeries;
		_com = baseTimeSeries.CenterOfMass();
		_origStdDev = baseTimeSeries.ComputeStandardDeviation();
		_newStdDev = newStandardDeviation;
	}

	public void Shift(TimeSpan deltaTime)
	{
		throw new NotImplementedException();
	}

	public DateTime CenterOfMass()
	{
		throw new NotImplementedException();
	}

	public DateTime TimeToAccumulate(double accumulatedYValue, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		throw new NotImplementedException();
	}

	public double SampleAccumulated(DateTime dt)
	{
		throw new NotImplementedException();
	}

	public double SampleAccumulatedDuring(DateTime start, DateTime end)
	{
		throw new NotImplementedException();
	}

	public TimeSpan ComputeStandardDeviation()
	{
		throw new NotImplementedException();
	}

	public void ComputeStandardDeviation(out TimeSpan leftStdDev, out TimeSpan rightStdDev)
	{
		throw new NotImplementedException();
	}

	public IAccumulatedTimeSeries Clone()
	{
		throw new NotImplementedException();
	}

	public double SampleRate(DateTime dt, Time rateTimeDenominator)
	{
		throw new NotImplementedException();
	}

	public PeriodAverageTimeSeries ResampleToPeriodAverage(IReadOnlyList<DateTime> newTimes, Time rateTimeDenominator)
	{
		throw new NotImplementedException();
	}

	public DateTime TimeToAccumulateRatio(double ratio, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		throw new NotImplementedException();
	}

	public void Scale(double scaleFactor)
	{
		throw new NotImplementedException();
	}

	public double Sample(DateTime time)
	{
		throw new NotImplementedException();
	}

	public double Sample(int pfIdx)
	{
		throw new NotImplementedException();
	}

	public double[] Sample(IList<DateTime> times)
	{
		throw new NotImplementedException();
	}
}
