using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public struct TimeWindow
{
	public DateTime StartTime { get; init; }

	public DateTime EndTime { get; init; }

	public bool StartTimeIsInclusive => true;

	public bool EndTimeIsInclusive => true;

	public bool IsInfinite
	{
		get
		{
			if (!(StartTime == DateTime.MinValue))
			{
				return EndTime == DateTime.MaxValue;
			}
			return true;
		}
	}

	public bool IsFinite => !IsInfinite;

	public static TimeWindow Infinite => new TimeWindow(DateTime.MinValue, DateTime.MaxValue);

	public TimeWindow(DateTime start, DateTime end)
	{
		StartTime = start;
		EndTime = end;
	}

	public DateTime[] Discretize(TimeSpan delta, bool includeFinalValuePastEndTime = false)
	{
		AssertFinite();
		throw new NotImplementedException();
	}

	private void AssertFinite()
	{
		if (IsInfinite)
		{
			throw new NotSupportedException("Cannot discretize an infinite time-window.");
		}
	}
}
