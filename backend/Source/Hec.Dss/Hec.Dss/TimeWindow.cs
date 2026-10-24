using System;
using System.Collections.Generic;

namespace Hec.Dss;

public static class TimeWindow
{
	public enum TimeWindowBehavior
	{
		StrictlyInclusive,
		Cover
	}

	public enum ConsecutiveValueCompression
	{
		None,
		NoData,
		ZeroAndNoData,
		AnyValue
	}

	private static string[] TimeIntervals7 = new string[39]
	{
		"1Year", "1Month", "Semi-Month", "Tri-Month", "1Week", "1Day", "12Hour", "8Hour", "6Hour", "4Hour",
		"3Hour", "2Hour", "1Hour", "30Minute", "20Minute", "15Minute", "12Minute", "10Minute", "6Minute", "5Minute",
		"4Minute", "3Minute", "2Minute", "1Minute", "30Second", "20Second", "15Second", "10Second", "6Second", "5Second",
		"4Second", "3Second", "2Second", "1Second", "IR-Century", "IR-Decade", "IR-Year", "IR-Month", "IR-Day"
	};

	private static string[] TimeIntervals6 = new string[29]
	{
		"1YEAR", "1MON", "SEMI-MONTH", "TRI-MONTH", "1WEEK", "1DAY", "12HOUR", "8HOUR", "6HOUR", "4HOUR",
		"3HOUR", "2HOUR", "1HOUR", "30MIN", "20MIN", "15MIN", "12MIN", "10MIN", "6MIN", "5MIN",
		"4MIN", "3MIN", "2MIN", "1MIN", "IR-CENTURY", "IR-DECADE", "IR-YEAR", "IR-MONTH", "IR-DAY"
	};

	private static int[] TimeIntervalSeconds7 = new int[39]
	{
		31536000, 2592000, 1296000, 864000, 604800, 86400, 43200, 28800, 21600, 14400,
		10800, 7200, 3600, 1800, 1200, 900, 720, 600, 360, 300,
		240, 180, 120, 60, 30, 20, 15, 10, 6, 5,
		4, 3, 2, 1, -5, -4, -3, -2, -1
	};

	private static int[] TimeIntervalSeconds6 = new int[29]
	{
		31536000, 2592000, 1296000, 864000, 604800, 86400, 43200, 28800, 21600, 14400,
		10800, 7200, 3600, 1800, 1200, 900, 720, 600, 360, 300,
		240, 180, 120, 60, -5, -4, -3, -2, -1
	};

	public static TimeSeries Trim(TimeSeries ts, DateTime t1, DateTime t2)
	{
		if (ValidWindowForTrim(ts, t1, t2))
		{
			TrimLeft(ts, t1, t2);
			TrimRight(ts, t1, t2);
		}
		return ts;
	}

	private static bool ValidWindowForTrim(TimeSeries ts, DateTime t1, DateTime t2)
	{
		if (ts.Count == 0)
		{
			return false;
		}
		if (t2 < t1)
		{
			return false;
		}
		DateTime dateTime = ts.Times[ts.Count - 1];
		if (t1 > dateTime)
		{
			return false;
		}
		if (t2 < ts.StartDateTime)
		{
			return false;
		}
		return true;
	}

	private static void RemoveRange(TimeSeries ts, int Start, int End)
	{
		List<DateTime> times = new List<DateTime>(ts.Times);
		List<double> doubleList = new List<double>(ts.Values);
		times.RemoveRange(Start, End);
		doubleList.RemoveRange(Start, End);
		ts.Times = times.ToArray();
		ts.Values = doubleList.ToArray();
	}

	private static void TrimLeft(TimeSeries ts, DateTime t1, DateTime t2)
	{
		if (ts.Count != 0 && !(t1 < ts.StartDateTime))
		{
			int index = Array.FindIndex(ts.Times, (DateTime dateTime) => dateTime >= t1);
			if (ts.Times[index] != t1)
			{
				index--;
			}
			RemoveRange(ts, 0, index);
		}
	}

	private static void TrimRight(TimeSeries ts, DateTime t1, DateTime t2)
	{
		if (ts.Count != 0 && !(t2 > ts.Times[ts.Count - 1]))
		{
			int lastIndex = Array.FindLastIndex(ts.Times, (DateTime dateTime) => dateTime <= t2);
			lastIndex = ((!(ts.Times[lastIndex] == t2)) ? (lastIndex + 2) : (lastIndex + 1));
			RemoveRange(ts, lastIndex, ts.Times.Length - lastIndex);
		}
	}

	internal static int SecondsInInterval(TimeSeries ts)
	{
		return SecondsInInterval(ts.Path);
	}

	internal static int SecondsInInterval(DssPath dssPath)
	{
		int timeIntervalSeconds7Term = 0;
		for (int i = 0; i < TimeIntervals7.Length; i++)
		{
			if (string.Compare(dssPath.Epart.TrimStart('~'), TimeIntervals7[i], ignoreCase: true) == 0)
			{
				timeIntervalSeconds7Term = TimeIntervalSeconds7[i];
				break;
			}
		}
		if (timeIntervalSeconds7Term == 0)
		{
			for (int j = 0; j < TimeIntervals6.Length; j++)
			{
				if (string.Compare(dssPath.Epart.TrimStart('~'), TimeIntervals6[j], ignoreCase: true) == 0)
				{
					timeIntervalSeconds7Term = TimeIntervalSeconds6[j];
					break;
				}
			}
		}
		return timeIntervalSeconds7Term;
	}

	public static string GetInterval(TimeSeries ts)
	{
		TimeSpan timeSpan = ts.TimeSpanInterval();
		int indexOf = Array.IndexOf(TimeIntervalSeconds7, (int)timeSpan.TotalSeconds);
		try
		{
			return TimeIntervals7[indexOf];
		}
		catch (IndexOutOfRangeException)
		{
			return "IR-YEAR";
		}
	}

	public static string GetInterval(TimeSpan ts)
	{
		int indexOf = Array.IndexOf(TimeIntervalSeconds7, (int)ts.TotalSeconds);
		try
		{
			return TimeIntervals7[indexOf];
		}
		catch (IndexOutOfRangeException)
		{
			return "IR-YEAR";
		}
	}
}
