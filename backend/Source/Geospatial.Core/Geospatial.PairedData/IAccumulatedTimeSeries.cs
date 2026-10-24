using System;
using System.Collections.Generic;
using Utility.Units;

namespace Geospatial.PairedData;

public interface IAccumulatedTimeSeries : IDiscreteTimeSeries, ITimeSeries
{
	IReadOnlyList<double> AccumulatedValues { get; }

	DateTime TimeToAccumulate(double accumulatedYValue, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First);

	double SampleAccumulated(DateTime dt);

	double SampleAccumulatedDuring(DateTime start, DateTime end);

	double SampleRate(DateTime dt, Time rateTimeDenominator);

	TimeSpan ComputeStandardDeviation();

	void ComputeStandardDeviation(out TimeSpan leftStdDev, out TimeSpan rightStdDev);

	PeriodAverageTimeSeries ResampleToPeriodAverage(IReadOnlyList<DateTime> newTimes, Time rateTimeDenominator);

	IAccumulatedTimeSeries Clone();
}
