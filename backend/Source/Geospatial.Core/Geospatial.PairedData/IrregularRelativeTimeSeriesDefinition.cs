using System;
using System.Diagnostics;
using System.Linq;
using Utility.Hashing;

namespace Geospatial.PairedData;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class IrregularRelativeTimeSeriesDefinition : TimeSeriesDefinitionBase
{
	[HashContributor]
	public Memory<TimeSpan> Times { get; set; }

	public override int Count => Times.Length;

	public IrregularRelativeTimeSeriesDefinition(TimeSpan[] ts)
	{
		Times = ts.ToArray();
	}

	private IrregularRelativeTimeSeriesDefinition()
	{
	}

	public static IrregularRelativeTimeSeriesDefinition FromSeconds(Span<double> seconds)
	{
		IrregularRelativeTimeSeriesDefinition irregularRelativeTimeSeriesDefinition = new IrregularRelativeTimeSeriesDefinition();
		TimeSpan[] times = new TimeSpan[seconds.Length];
		for (int i = 0; i < seconds.Length; i++)
		{
			times[i] = TimeSpan.FromSeconds(seconds[i]);
		}
		irregularRelativeTimeSeriesDefinition.Times = times;
		return irregularRelativeTimeSeriesDefinition;
	}

	public static IrregularRelativeTimeSeriesDefinition FromMinutes(Span<double> minutes)
	{
		IrregularRelativeTimeSeriesDefinition irregularRelativeTimeSeriesDefinition = new IrregularRelativeTimeSeriesDefinition();
		TimeSpan[] times = new TimeSpan[minutes.Length];
		for (int i = 0; i < minutes.Length; i++)
		{
			times[i] = TimeSpan.FromMinutes(minutes[i]);
		}
		irregularRelativeTimeSeriesDefinition.Times = times;
		return irregularRelativeTimeSeriesDefinition;
	}

	public static IrregularRelativeTimeSeriesDefinition FromHours(Span<double> hours)
	{
		IrregularRelativeTimeSeriesDefinition irregularRelativeTimeSeriesDefinition = new IrregularRelativeTimeSeriesDefinition();
		TimeSpan[] times = new TimeSpan[hours.Length];
		for (int i = 0; i < hours.Length; i++)
		{
			times[i] = TimeSpan.FromHours(hours[i]);
		}
		irregularRelativeTimeSeriesDefinition.Times = times;
		return irregularRelativeTimeSeriesDefinition;
	}

	public override void GetRawTimeValues(Memory<long> times)
	{
		if (times.Length != Count)
		{
			throw new ArgumentException("Array presized incorrectly.");
		}
		Span<long> timesSpan = times.Span;
		Span<TimeSpan> times2 = Times.Span;
		for (int i = 0; i < Count; i++)
		{
			timesSpan[i] = times2[i].Ticks;
		}
	}

	public override DateTime[] GetAbsoluteTimes(int nOrdinates, DateTime startTime)
	{
		Memory<TimeSpan> times = Times;
		if (times.Length != nOrdinates)
		{
			throw new Exception("# Ordinates doesn't match given DT array.");
		}
		times = Times;
		Span<TimeSpan> timesSpan = times.Span;
		DateTime[] absoluteTimes = new DateTime[timesSpan.Length];
		for (int i = 0; i < timesSpan.Length; i++)
		{
			absoluteTimes[i] = SafeAddSeconds(startTime, timesSpan[i].TotalSeconds);
		}
		return absoluteTimes;
	}

	public override DateTime TimeFor(DateTime injectedStartTime, int index)
	{
		if (index >= Count)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		return injectedStartTime + Times.Span[index];
	}

	public DateTime SafeAddSeconds(DateTime offset, double seconds)
	{
		if (seconds > 0.0)
		{
			double totalSeconds = (DateTime.MaxValue - offset).TotalSeconds;
			if (seconds < totalSeconds)
			{
				return offset.AddSeconds(seconds);
			}
			return DateTime.MaxValue;
		}
		if (seconds < 0.0)
		{
			double totalSecondsOffset = 0.0 - (offset - DateTime.MinValue).TotalSeconds;
			if (seconds < totalSecondsOffset)
			{
				return DateTime.MinValue;
			}
			return offset.AddSeconds(seconds);
		}
		return offset;
	}

	private string GetDebuggerDisplay()
	{
		return ToString();
	}

	public override bool CoversTimeWindow(DateTime startTime, TimeSpan duration, int nOrdinates, DateTime inheritedStartTime, ProfileInterpolationMethod interp = ProfileInterpolationMethod.Linear)
	{
		Memory<TimeSpan> times = Times;
		if (times.Length < 2)
		{
			return false;
		}
		times = Times;
		Span<TimeSpan> timesSpan = times.Span;
		TimeSpan timeSpan = timesSpan[0];
		TimeSpan timeSpan2 = timesSpan[timesSpan.Length - 1];
		if (timeSpan.TotalSeconds <= 0.0)
		{
			return timeSpan2 >= duration;
		}
		return false;
	}
}
