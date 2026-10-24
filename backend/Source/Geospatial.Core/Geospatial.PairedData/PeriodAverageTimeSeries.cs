using System;
using System.Collections.Generic;
using Utility.Units;

namespace Geospatial.PairedData;

public class PeriodAverageTimeSeries : RateTimeSeries
{
	public PeriodAverageTimeSeries(Time rateDenominator)
		: base(rateDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment)
	{
	}

	public PeriodAverageTimeSeries(int capacity, Time rateDenominator)
		: base(capacity, rateDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment)
	{
	}

	public PeriodAverageTimeSeries(IEnumerable<DateTime> times, IEnumerable<double> values, Time rateDenominator)
		: base(times, values, rateDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment)
	{
	}
}
