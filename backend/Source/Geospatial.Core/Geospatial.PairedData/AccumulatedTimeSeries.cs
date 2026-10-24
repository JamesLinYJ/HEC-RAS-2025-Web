using System;
using System.Collections.Generic;
using Utility.Units;

namespace Geospatial.PairedData;

public class AccumulatedTimeSeries : TimeSeries, IAccumulatedTimeSeries, IDiscreteTimeSeries, ITimeSeries
{
	public IReadOnlyList<double> AccumulatedValues
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public TimeSpan ComputeStandardDeviation()
	{
		throw new NotImplementedException();
	}

	public void ComputeStandardDeviation(out TimeSpan leftStdDev, out TimeSpan rightStdDev)
	{
		throw new NotImplementedException();
	}

	public PeriodAverageTimeSeries ResampleToPeriodAverage(IReadOnlyList<DateTime> newTimes, Time rateTimeDenominator)
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

	public double SampleRate(DateTime dt, Time rateTimeDenominator)
	{
		throw new NotImplementedException();
	}

	public DateTime TimeToAccumulate(double accumulatedYValue, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		throw new NotImplementedException();
	}

	IAccumulatedTimeSeries IAccumulatedTimeSeries.Clone()
	{
		throw new NotImplementedException();
	}
}
