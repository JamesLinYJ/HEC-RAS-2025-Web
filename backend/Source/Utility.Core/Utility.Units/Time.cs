using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utility.Units;

public class Time : BaseUnitType
{
	private enum DateAxisInterval
	{
		Seconds,
		Minutes,
		Hours,
		Days,
		Months,
		Years
	}

	public const string DefaultDatetimeFormat = "yyyy-MM-dd HH:mm:ss";

	public const string DefaultDateFormat = "yyyy-MM-dd";

	public const string DefaultTimeFormat = "HH:mm:ss";

	public const string Iso8601UtcFormat = "yyyy-MM-ddTHH:mm:ssZ";

	public const string DatetimeFormatSeconds_FileSystem = "yyyy-MMM-dd HH-mm-ss";

	public const string DatetimeFormatSeconds_S3 = "yyyy-MMM-dd-HH-mm-ss";

	public const string DatetimeFormatSeconds = "yyyy-MMM-dd HH:mm:ss";

	public const string DatetimeFormatMinutes = "yyyy-MMM-dd HH:mm";

	public const string DatetimeFormatHours = "yyyy-MMM-dd HH:mm";

	public const string DatetimeFormatDays = "yyyy-MMM-dd";

	public const string DatetimeFormatMonths = "yyyy-MMM";

	public const string DatetimeFormatYears = "yyyy-MMM";

	public const int SecondsPerMinute = 60;

	public const int SecondsPerHour = 3600;

	public static readonly DateTime UnixEpoch;

	public static readonly Time Milliseconds;

	public static readonly Time Seconds;

	public static readonly Time Minutes;

	public static readonly Time Hours;

	public static readonly Time Days;

	private static int[] BestSeconds;

	private static int[] BestMinutes;

	private static int[] BestHours;

	private static int[] BestDays;

	private static int[] BestMonths;

	private static int[] BestYears;

	public long Ticks => TicksPerUnitTime(this);

	public static string CurrentDateFormatted()
	{
		return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
	}

	public static string CurrentDateFormattedForFileSystem()
	{
		return DateTime.Now.ToString("yyyy-MMM-dd HH-mm-ss");
	}

	public static string CurrentDateFilenameSafe()
	{
		return DateTime.Now.ToString("yyyy-MM-dd HH_mm_ss");
	}

	static Time()
	{
		UnixEpoch = new DateTime(1970, 1, 1);
		Milliseconds = new Time("Millisecond", new string[5] { "ms", "msec", "msecs", "millis", "milliseconds" }, (double ms) => ms / 1000.0, (double sec) => sec * 1000.0);
		Seconds = new Time("Second", new string[4] { "s", "sec", "secs", "seconds" }, null, null);
		Minutes = new Time("Minute", new string[4] { "m", "min", "mins", "minutes" }, (double min) => min * 60.0, (double sec) => sec / 60.0);
		Hours = new Time("Hour", new string[3] { "hours", "hrs", "h" }, (double hr) => hr * 60.0 * 60.0, (double sec) => sec / 3600.0);
		Days = new Time("Day", new string[2] { "days", "d" }, (double day) => day * 24.0 * 60.0 * 60.0, (double sec) => sec / 86400.0);
		BestSeconds = new int[5] { 1, 2, 5, 10, 15 };
		BestMinutes = new int[5] { 1, 2, 5, 10, 15 };
		BestHours = new int[5] { 1, 2, 3, 6, 12 };
		BestDays = new int[4] { 1, 2, 5, 10 };
		BestMonths = new int[5] { 1, 2, 3, 4, 6 };
		BestYears = new int[10] { 1, 2, 5, 10, 25, 50, 100, 200, 500, 1000 };
	}

	private Time(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Time t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static List<Time> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Time>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<Time>()
			select t.Name).ToList();
	}

	public static long TicksPerUnitTime(Time time)
	{
		if (time == Seconds)
		{
			return 10000000L;
		}
		if (time == Minutes)
		{
			return 600000000L;
		}
		if (time == Hours)
		{
			return 36000000000L;
		}
		if (time == Days)
		{
			return 864000000000L;
		}
		throw new NotImplementedException();
	}

	public double FromTimespan(TimeSpan ts)
	{
		return FromTimespan(this, ts);
	}

	public static double FromTimespan(Time time, TimeSpan ts)
	{
		if (time == Seconds)
		{
			return ts.TotalSeconds;
		}
		if (time == Minutes)
		{
			return ts.TotalMinutes;
		}
		if (time == Hours)
		{
			return ts.TotalHours;
		}
		if (time == Days)
		{
			return ts.TotalDays;
		}
		throw new NotImplementedException();
	}

	public TimeSpan ToTimespan(double doubleValue)
	{
		return TimeSpan.FromDays(ConvertTo(Days, doubleValue));
	}

	public static Time TryParse(string timeUnit)
	{
		return BaseUnitType.BaseParse<Time>(timeUnit);
	}

	public static bool TryParseTimestep(string timestep, out TimeSpan ts)
	{
		if (string.IsNullOrEmpty(timestep))
		{
			ts = default;
			return false;
		}
		double result;
		string timeUnit;
		if (timestep.Contains(' '))
		{
			string[] parts = timestep.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 2)
			{
				ts = default;
				return false;
			}
			if (!double.TryParse(parts[0], out result))
			{
				ts = default;
				return false;
			}
			timeUnit = parts[1];
		}
		else
		{
			int ordinal = -1;
			for (int i = 0; i < timestep.Length; i++)
			{
				if (char.IsLetter(timestep[i]))
				{
					ordinal = i;
					break;
				}
			}
			if (ordinal == -1)
			{
				ts = default;
				return false;
			}
			if (!double.TryParse(timestep.AsSpan(0, ordinal), out result))
			{
				ts = default;
				return false;
			}
			timeUnit = timestep.AsSpan(ordinal).ToString();
		}
		Time time = TryParse(timeUnit);
		if (time == null)
		{
			ts = default;
			return false;
		}
		ts = time.ToTimespan(result);
		return true;
	}

	public static void SplitSeconds(float sec, out int wholeSec, out int ms)
	{
		wholeSec = (int)Math.Floor(sec);
		ms = (int)Math.Round((sec - (float)wholeSec) * 1000f);
		if (ms < 0)
		{
			ms = 0;
		}
		else if (ms > 999)
		{
			ms = 999;
		}
	}

	public static long ConvertToUnixTime(DateTime dt)
	{
		return (long)(dt.ToUniversalTime() - UnixEpoch).TotalSeconds;
	}

	public static DateTime ConvertToLocalTime(long unixTime)
	{
		return DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;
	}

	public static DateTime[] TryParseFormattedDateTimes(IList<string> timeStrings)
	{
		DateTime[] dateTimes = new DateTime[timeStrings.Count];
		for (int i = 0; i < timeStrings.Count; i++)
		{
			if (DateTime.TryParseExact(timeStrings[i], "yyyy-MM-dd HH:mm:ss", null, DateTimeStyles.None, out var result))
			{
				dateTimes[i] = result;
			}
			else
			{
				dateTimes[i] = DateTime.MinValue;
			}
		}
		return dateTimes;
	}

	private static int TargetReasonable(float computed, int targetTickCount, int[] options)
	{
		int intervalIndex = 0;
		float computed2 = computed / (float)options[0];
		for (int i = 1; i < options.Length; i++)
		{
			float optionsTerm = computed / (float)options[i];
			if (Math.Abs(optionsTerm - (float)targetTickCount) < Math.Abs(computed2 - (float)targetTickCount))
			{
				intervalIndex = i;
				computed2 = optionsTerm;
			}
		}
		return options[intervalIndex];
	}

	private static List<Tuple<int, DateAxisInterval, TimeSpan>> PossibleTicks()
	{
		List<Tuple<int, DateAxisInterval, TimeSpan>> tupleList = new List<Tuple<int, DateAxisInterval, TimeSpan>>();
		for (int i = 0; i < BestSeconds.Length; i++)
		{
			int secondsIds = BestSeconds[i];
			tupleList.Add(new Tuple<int, DateAxisInterval, TimeSpan>(secondsIds, DateAxisInterval.Seconds, TimeSpan.FromSeconds(secondsIds)));
		}
		for (int j = 0; j < BestMinutes.Length; j++)
		{
			int minutesIds = BestMinutes[j];
			tupleList.Add(new Tuple<int, DateAxisInterval, TimeSpan>(minutesIds, DateAxisInterval.Minutes, TimeSpan.FromMinutes(minutesIds)));
		}
		for (int k = 0; k < BestHours.Length; k++)
		{
			int hoursIds = BestHours[k];
			tupleList.Add(new Tuple<int, DateAxisInterval, TimeSpan>(hoursIds, DateAxisInterval.Hours, TimeSpan.FromHours(hoursIds)));
		}
		for (int l = 0; l < BestDays.Length; l++)
		{
			int daysIds = BestDays[l];
			tupleList.Add(new Tuple<int, DateAxisInterval, TimeSpan>(daysIds, DateAxisInterval.Days, TimeSpan.FromDays(daysIds)));
		}
		for (int m = 0; m < BestMonths.Length; m++)
		{
			int monthsIds = BestMonths[m];
			tupleList.Add(new Tuple<int, DateAxisInterval, TimeSpan>(monthsIds, DateAxisInterval.Months, TimeSpan.FromDays(monthsIds * 30)));
		}
		for (int n = 0; n < BestYears.Length; n++)
		{
			int yearsIds = BestYears[n];
			tupleList.Add(new Tuple<int, DateAxisInterval, TimeSpan>(yearsIds, DateAxisInterval.Years, TimeSpan.FromDays(yearsIds * 365)));
		}
		return tupleList;
	}

	private static DateTime Add(DateTime dt, int ordinal, DateAxisInterval interval)
	{
		switch (interval)
		{
		case DateAxisInterval.Seconds:
			return dt.AddSeconds(ordinal);
		case DateAxisInterval.Minutes:
			return dt.AddMinutes(ordinal);
		case DateAxisInterval.Hours:
			return dt.AddHours(ordinal);
		case DateAxisInterval.Days:
			return dt.AddDays(ordinal);
		case DateAxisInterval.Months:
		{
			int monthDtTerm = dt.Month - 1 + ordinal;
			int ordinal2 = monthDtTerm / 12;
			int ordinal3 = monthDtTerm % 12;
			ordinal3++;
			return new DateTime(dt.Year + ordinal2, ordinal3, dt.Day, dt.Hour, dt.Minute, dt.Second);
		}
		case DateAxisInterval.Years:
			return new DateTime(dt.Year + ordinal, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
		default:
			return dt;
		}
	}

	private static DateTime Nearest(DateTime dt, int ordinal, DateAxisInterval interval)
	{
		switch (interval)
		{
		case DateAxisInterval.Seconds:
		{
			int second = dt.Second;
			int secondTerm = Round(second, ordinal) - second;
			return dt.AddSeconds(secondTerm);
		}
		case DateAxisInterval.Minutes:
		{
			int minute = dt.Minute;
			int minuteTerm = Round(minute, ordinal) - minute;
			return dt.AddMinutes(minuteTerm);
		}
		case DateAxisInterval.Hours:
		{
			int hour = dt.Hour;
			int hourTerm = Round(hour, ordinal) - hour;
			return dt.AddHours(hourTerm);
		}
		case DateAxisInterval.Days:
		{
			int day = dt.Day;
			int dayTerm = Round(day, ordinal) - day;
			return dt.AddDays(dayTerm);
		}
		case DateAxisInterval.Months:
		{
			int monthDtTerm = Round(dt.Month, ordinal);
			monthDtTerm++;
			if (monthDtTerm > 12)
			{
				return new DateTime(dt.Year + 1, monthDtTerm - 12, 1);
			}
			return new DateTime(dt.Year, monthDtTerm, 1);
		}
		case DateAxisInterval.Years:
			return new DateTime(Round(dt.Year, ordinal), 1, 1);
		default:
			return dt;
		}
		static int Round(int amt, int modBase)
		{
			int amtWithModBase = amt % modBase;
			if (amtWithModBase == 0)
			{
				return amt;
			}
			if ((float)amtWithModBase >= (float)modBase / 2f)
			{
				return RoundUp(amt, modBase);
			}
			return RoundDown(amt, modBase);
		}
		static int RoundDown(int amt, int mod)
		{
			return amt / mod * mod;
		}
		static int RoundUp(int amt, int mod)
		{
			return (amt / mod + 1) * mod;
		}
	}

	private static DateTime NearestTarget(DateTime dt, int ordinal, DateAxisInterval interval, bool findSmaller)
	{
		DateTime dateTime = Nearest(dt, ordinal, interval);
		if (findSmaller)
		{
			if (dateTime > dt)
			{
				return Add(dateTime, -ordinal, interval);
			}
		}
		else if (dateTime < dt)
		{
			return Add(dateTime, ordinal, interval);
		}
		return dateTime;
	}

	private static void ClosestTick(TimeSpan ts, out DateAxisInterval interval, out int tupleTerm, out TimeSpan approxTS)
	{
		List<Tuple<int, DateAxisInterval, TimeSpan>> possibleTicks = PossibleTicks();
		double maxValue = double.MaxValue;
		Tuple<int, DateAxisInterval, TimeSpan> tuple = null;
		foreach (Tuple<int, DateAxisInterval, TimeSpan> possibleTick in possibleTicks)
		{
			double maxValue2 = Math.Abs(possibleTick.Item3.TotalHours - ts.TotalHours);
			if (maxValue2 < maxValue)
			{
				tuple = possibleTick;
				maxValue = maxValue2;
			}
		}
		interval = tuple.Item2;
		tupleTerm = tuple.Item1;
		approxTS = tuple.Item3;
	}

	public static void DateAxis(DateTime startTime, DateTime endTime, int desiredMajorTicks, bool spanWindow, out DateTime[] majorTicks, out DateTime[] minorTicks, out string[] majorTickLabels)
	{
		if (endTime <= startTime)
		{
			throw new Exception("endTime must be greater than startTime");
		}
		int ordinal = 5;
		ClosestTick(TimeSpan.FromHours((endTime - startTime).TotalHours / (double)desiredMajorTicks), out var interval, out var ordinal2, out var approxTS);
		ClosestTick(TimeSpan.FromHours(approxTS.TotalHours / (double)ordinal), out var interval2, out var ordinal3, out var _);
		DateTime dateTime = NearestTarget(startTime, ordinal2, interval, spanWindow);
		DateTime dateTime2 = NearestTarget(dateTime, ordinal3, interval2, findSmaller: false);
		List<DateTime> dateTimes = new List<DateTime>();
		List<DateTime> dateTimesList = new List<DateTime>();
		List<string> stringList = new List<string>();
		string dtFormat = DTFormat(interval);
		dateTimes.Add(dateTime);
		stringList.Add(dateTime.ToString(dtFormat));
		while (true)
		{
			dateTime = Add(dateTime, ordinal2, interval);
			if (dateTime > endTime)
			{
				break;
			}
			dateTimes.Add(dateTime);
			stringList.Add(dateTime.ToString(dtFormat));
		}
		if (spanWindow)
		{
			dateTimes.Add(dateTime);
			stringList.Add(dateTime.ToString(dtFormat));
		}
		dateTimesList.Add(dateTime2);
		while (true)
		{
			dateTime2 = Add(dateTime2, ordinal3, interval2);
			if (dateTime2 > endTime)
			{
				break;
			}
			dateTimesList.Add(dateTime2);
		}
		if (spanWindow)
		{
			dateTimesList.Add(dateTime2);
		}
		majorTicks = dateTimes.ToArray();
		majorTickLabels = stringList.ToArray();
		minorTicks = dateTimesList.ToArray();
	}

	public static bool TryMakeDateAxis(DateTime startTime, DateTime endTime, int desiredMajorTicks, bool spanWindow, out DateTime[] majorTicks, out DateTime[] minorTicks, out string[] majorTickLabels)
	{
		try
		{
			DateAxis(startTime, endTime, desiredMajorTicks, spanWindow, out majorTicks, out minorTicks, out majorTickLabels);
			return true;
		}
		catch
		{
			majorTicks = null;
			minorTicks = null;
			majorTickLabels = null;
			return false;
		}
	}

	private static string DTFormat(DateAxisInterval interval)
	{
		return interval switch
		{
			DateAxisInterval.Seconds => "yyyy-MMM-dd HH:mm:ss", 
			DateAxisInterval.Minutes => "yyyy-MMM-dd HH:mm", 
			DateAxisInterval.Hours => "yyyy-MMM-dd HH:mm", 
			DateAxisInterval.Days => "yyyy-MMM-dd", 
			DateAxisInterval.Months => "yyyy-MMM", 
			DateAxisInterval.Years => "yyyy-MMM", 
			_ => "", 
		};
	}
}
