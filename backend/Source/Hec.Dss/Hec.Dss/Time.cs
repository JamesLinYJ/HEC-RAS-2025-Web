using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hec.Dss;

public static class Time
{
	private const string HecDateFormat = "ddMMMyyyy";

	private static Regex hecDateRegex = new Regex("\\d\\d\\w{3}\\d\\d\\d\\d", RegexOptions.Compiled);

	private static CultureInfo s_cultureInfo = new CultureInfo("en-US");

	private const int UNDEFINED_TIME = -2147483647;

	internal static bool IsUndefinedTime(int t)
	{
		return t == -2147483647;
	}

	private static string ToHecDateString(DateTime t)
	{
		return t.ToString("ddMMMyyyy", s_cultureInfo);
	}

	public static DateTime ConvertFromHecDateTime(string dateString, string timeString)
	{
		DateTime t = DateTime.ParseExact(dateString, "ddMMMyyyy", CultureInfo.InvariantCulture);
		TryParseTimeString(timeString, out var hr, out var min, out var sec);
		return IncrementDateTime(t, hr, min, sec);
	}

	private static DateTime IncrementDateTime(DateTime t, int hr, int min, int sec)
	{
		t = ((hr != 24) ? t.AddHours(hr).AddMinutes(min).AddSeconds(sec) : t.AddDays(1.0));
		return t;
	}

	public static bool TryConvertFromHecDateTime(string dateString, string timeString, out DateTime date)
	{
		if (!DateTime.TryParseExact(dateString, "ddMMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
		{
			return false;
		}
		if (!TryParseTimeString(timeString, out var hr, out var min, out var sec))
		{
			return false;
		}
		date = IncrementDateTime(date, hr, min, sec);
		return true;
	}

	public static DateTime ConvertFromHecDateTime(string dateTimeString)
	{
		if (!TryConvertFromHecDateTime(dateTimeString, out var date))
		{
			throw new FormatException("invalid Hec Date format '" + dateTimeString + "'");
		}
		return date;
	}

	internal static string JulianToHecDate(int julian)
	{
		int year = 0;
		int month = 0;
		int day = 0;
		DssNative.hec_dss_julianToYearMonthDay(julian, ref year, ref month, ref day);
		return ToHecDateString(new DateTime(year, month, day));
	}

	public static bool TryConvertFromHecDateTime(string dateTimeString, out DateTime date)
	{
		date = default;
		bool tryParseExact = false;
		dateTimeString = dateTimeString.Trim();
		if (!hecDateRegex.IsMatch(dateTimeString))
		{
			return false;
		}
		string[] parts = dateTimeString.Trim().Split(':');
		if (parts.Length == 2 && parts[0].Length == 9 && parts[1].Length == 4)
		{
			return TryConvertFromHecDateTime(parts[0], parts[1], out date);
		}
		if (dateTimeString.Length == 9)
		{
			tryParseExact = DateTime.TryParseExact(dateTimeString, "ddMMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
		}
		if (!tryParseExact)
		{
			parts = dateTimeString.Split(default(ReadOnlySpan<char>));
			if (parts.Length == 2)
			{
				DateTime t = DateTime.ParseExact(parts[0], "ddMMMyyyy", CultureInfo.InvariantCulture);
				if (!TryParseTimeString(parts[1], out var hr, out var min, out var sec))
				{
					date = default;
					return false;
				}
				t = IncrementDateTime(t, hr, min, sec);
				date = t;
				tryParseExact = true;
			}
		}
		return tryParseExact;
	}

	private static bool TryParseTimeString(string time, out int hr, out int min, out int sec)
	{
		sec = 0;
		hr = 0;
		min = 0;
		string trim = time.Trim();
		if (trim.Length < 4)
		{
			return false;
		}
		if (trim.Length == 4)
		{
			if (!int.TryParse(trim.Substring(0, 2), out hr))
			{
				return false;
			}
			if (!int.TryParse(trim.Substring(2, 2), out min))
			{
				return false;
			}
			return true;
		}
		string[] parts = trim.Split(':');
		if (parts.Length >= 2 && parts.Length <= 3)
		{
			if (!int.TryParse(parts[0], out hr))
			{
				return false;
			}
			if (!int.TryParse(parts[1], out min))
			{
				return false;
			}
			if (parts.Length == 3 && !int.TryParse(parts[2], out sec))
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public static void DateTimeToHecDateTime(DateTime t, out string date, out string time)
	{
		if (t == default(DateTime))
		{
			date = "";
			time = "";
			return;
		}
		if (t == t.Date)
		{
			t = t.AddDays(-1.0);
			time = "2400";
		}
		else if (t.Second > 0)
		{
			time = t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		}
		else
		{
			time = t.ToString("HHmm", CultureInfo.InvariantCulture);
		}
		date = ToHecDateString(t);
	}

	internal static int[] ConvertDatesToHecInteger(DateTime[] times, int julianBaseDate = 0, int timeGranularitySeconds = 60)
	{
		int[] intValues = new int[times.Length];
		for (int i = 0; i < times.Length; i++)
		{
			int dateToJulian = DateToJulian(times[i]);
			int timeStringToSeconds = TimeStringToSeconds(times[i].ToString("HH:mm:ss", CultureInfo.InvariantCulture));
			intValues[i] = (dateToJulian - julianBaseDate) * (86400 / timeGranularitySeconds) + timeStringToSeconds / timeGranularitySeconds;
		}
		return intValues;
	}

	public static int TimeStringToSeconds(string time)
	{
		if (!TryParseTimeString(time, out var hr, out var min, out var sec))
		{
			return 0;
		}
		return hr * 3600 + min * 60 + sec;
	}

	public static int YearMonthDayToJulian(int year, int month, int day)
	{
		return DateToJulian(new DateTime(year, month, day));
	}

	internal static DateTime[] DateTimesFromJulianArray(int[] timesJulian, int timeGranularitySeconds, int julianBaseDate)
	{
		if (timesJulian == null)
		{
			throw new NullReferenceException("Time Series Times array was null.  Something didn't work right in DSS.");
		}
		DateTime[] dateTimes = new DateTime[timesJulian.Length];
		double scaledTimeGranularitySeconds = 86400.0 / (double)timeGranularitySeconds;
		for (int i = 0; i < dateTimes.Length; i++)
		{
			dateTimes[i] = DateTime.FromOADate((double)timesJulian[i] / scaledTimeGranularitySeconds + (double)julianBaseDate + 1.0);
		}
		return dateTimes;
	}

	public static int DateToJulian(string date)
	{
		return DssNative.hec_dss_dateToJulian(date);
	}

	internal static int DateToJulian(DateTime t)
	{
		return (int)(t.ToOADate() - 1.0);
	}

	internal static void JulianToHecDateTime(int julian, int seconds, out string date, out string time)
	{
		int year = 0;
		int month = 0;
		int day = 0;
		DssNative.hec_dss_julianToYearMonthDay(julian, ref year, ref month, ref day);
		DateTime t = new DateTime(year, month, day).AddSeconds(seconds);
		DateTimeToHecDateTime(t, out date, out time);
	}
}
