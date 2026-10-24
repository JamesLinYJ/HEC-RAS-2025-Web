using System;

namespace Hec.Dss;

public class TimeSeriesPoint
{
	public int Quality { get; set; }

	public DateTime DateTime { get; set; }

	public double Value { get; set; }

	public TimeSeriesPoint(DateTime dateTime, double doubleValue, int quality = -1)
	{
		DateTime = dateTime;
		Value = doubleValue;
		Quality = quality;
	}

	public override string ToString()
	{
		Time.DateTimeToHecDateTime(DateTime, out var date, out var time);
		return date + " " + time + ", " + Value.ToString("F2");
	}
}
