using System;
using System.Collections.Generic;
using Utility.Units;

namespace Utility.Extensions;

public static class TimeSpanExtensions
{
	public static string ToPrettyString(this TimeSpan ts)
	{
		if (ts.TotalMilliseconds == 0.0)
		{
			return "No Time";
		}
		List<string> stringList = new List<string>();
		if (Math.Abs(ts.Days) > 0)
		{
			stringList.Add($"{ts.Days}d");
		}
		if (Math.Abs(ts.Hours) > 0)
		{
			stringList.Add($"{ts.Hours}h");
		}
		if (Math.Abs(ts.Minutes) > 0)
		{
			stringList.Add($"{ts.Minutes}m");
		}
		if (Math.Abs(ts.Seconds) > 0)
		{
			stringList.Add($"{ts.Seconds}s");
		}
		if (Math.Abs(ts.Milliseconds) > 0)
		{
			stringList.Add($"{ts.Milliseconds}ms");
		}
		return string.Join(" ", stringList);
	}

	public static DateTime AddUnits(this DateTime input, Time timeUnits, double timeValue)
	{
		if (timeUnits == Time.Seconds)
		{
			return input.AddSeconds(timeValue);
		}
		if (timeUnits == Time.Minutes)
		{
			return input.AddMinutes(timeValue);
		}
		if (timeUnits == Time.Hours)
		{
			return input.AddHours(timeValue);
		}
		if (timeUnits == Time.Days)
		{
			return input.AddDays(timeValue);
		}
		throw new NotImplementedException();
	}
}
