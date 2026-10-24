using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.PairedData;

public static class AccumulatedTimeSeriesExtensions
{
	public static double TotalAccumulated(this IAccumulatedTimeSeries ts)
	{
		return ts.AccumulatedValues.LastOrDefault();
	}

	public static DateTime CenterOfMass(this IAccumulatedTimeSeries ts)
	{
		IReadOnlyList<double> accumulatedValues = ts.AccumulatedValues;
		double computed = 0.0;
		double accumulatedValuesTerm = accumulatedValues.Last();
		if (computed == accumulatedValuesTerm)
		{
			return ts.StartTime;
		}
		double computed2 = accumulatedValuesTerm - computed;
		return ts.TimeToAccumulate(computed2 * 0.5);
	}

	public static DateTime TimeToAccumulateRatio(this IAccumulatedTimeSeries ts, double ratio, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		if (ratio < 0.0 || ratio > 1.0)
		{
			throw new ArgumentOutOfRangeException("ratio");
		}
		double accumulatedValuesTsTerm = ts.AccumulatedValues.Last();
		return ts.TimeToAccumulate(accumulatedValuesTsTerm * ratio, tiebreaker);
	}
}
