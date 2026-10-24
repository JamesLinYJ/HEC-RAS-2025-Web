using System;

namespace Utility.Helpers;

public static class DateTimeHelpers
{
	public static bool HasEqualPartsTo(this DateTime time, DateTime other, bool compareYears = true, bool compareMonths = true, bool compareDays = true, bool compareHours = true, bool compareMinutes = true, bool compareSeconds = true, bool compareMilliseconds = false, bool compareMicroseconds = false)
	{
		if (compareYears && !time.Year.Equals(other.Year))
		{
			return false;
		}
		if (compareMonths && !time.Month.Equals(other.Month))
		{
			return false;
		}
		if (compareDays && !time.Day.Equals(other.Day))
		{
			return false;
		}
		if (compareHours && !time.Hour.Equals(other.Hour))
		{
			return false;
		}
		if (compareMinutes && !time.Minute.Equals(other.Minute))
		{
			return false;
		}
		if (compareSeconds && !time.Second.Equals(other.Second))
		{
			return false;
		}
		if (compareMilliseconds && !time.Millisecond.Equals(other.Millisecond))
		{
			return false;
		}
		if (compareMicroseconds && !time.Microsecond.Equals(other.Microsecond))
		{
			return false;
		}
		return true;
	}

	public static DateTime StripMillisecondsAndNanoseconds(this DateTime time)
	{
		return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second, time.Kind);
	}
}
