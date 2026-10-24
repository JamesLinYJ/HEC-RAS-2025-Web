using System;
using System.Collections.Generic;

namespace Ras.Mapping.ArrivalTime;

public class TimeSeriesExtrema : List<LocalExtremaValue>
{
	public TimeSeriesExtrema(int ct)
		: base(ct)
	{
	}

	public void AddRange(List<LocalExtrema> extrema, List<float> values)
	{
		if (extrema == null)
		{
			throw new ArgumentNullException("extrema");
		}
		if (values == null)
		{
			throw new ArgumentNullException("values");
		}
		if (extrema.Count != values.Count)
		{
			throw new ArgumentException("Extrema and values must have the same number of values");
		}
		throw new NotImplementedException();
	}

	public WindowSeries ToWindowSeries()
	{
		throw new NotImplementedException();
	}

	public WindowSeries Combine(TimeSeriesExtrema other)
	{
		return new MultiLocationTimeSeriesExtrema { this, other }.Combine();
	}
}
