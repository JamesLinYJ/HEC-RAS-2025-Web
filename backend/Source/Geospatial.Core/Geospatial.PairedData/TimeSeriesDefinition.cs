using System;
using System.Linq;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;
using Utility.Units;

namespace Geospatial.PairedData;

public class TimeSeriesDefinition : IDataHash
{
	public static readonly DateTime NullTimestamp = DateTime.MinValue;

	[HashContributor]
	[Metadata("TS Type")]
	public TimeSeriesType TimeSeriesType { get; set; }

	[HashContributor]
	[Metadata("Irregular Time Expressed As")]
	public ExpressedTimeType ExpressedTimeType { get; set; }

	[HashContributor]
	[DatasetName("IRTS Timesteps")]
	public DateTime[] IrregularAbsoluteTimes { get; set; }

	[HashContributor]
	[DatasetName("IRTS Times (Relative)")]
	public double[] IrregularRelativeTimes { get; set; }

	[HashContributor]
	[Metadata("RTS Time Interval")]
	public int RTSInterval { get; set; } = 1;

	[HashContributor]
	[Metadata("RTS Time Unit")]
	public string RTSUnit { get; set; }

	[HashContributor]
	public TimeSpan RTSTimeSpan
	{
		get
		{
			Time time = Time.TryParse(RTSUnit);
			if (time == null)
			{
				time = Time.Seconds;
			}
			return time.ToTimespan(Math.Max(0, RTSInterval));
		}
		set
		{
			if (value.TotalDays == (double)(int)value.TotalDays)
			{
				RTSInterval = (int)value.TotalDays;
				RTSUnit = Time.Days.Name;
			}
			else if (value.TotalHours == (double)(int)value.TotalHours)
			{
				RTSInterval = (int)value.TotalHours;
				RTSUnit = Time.Hours.Name;
			}
			else if (value.TotalMinutes == (double)(int)value.TotalMinutes)
			{
				RTSInterval = (int)value.TotalMinutes;
				RTSUnit = Time.Minutes.Name;
			}
			else if (value.TotalSeconds == (double)(int)value.TotalSeconds)
			{
				RTSInterval = (int)value.TotalSeconds;
				RTSUnit = Time.Seconds.Name;
			}
			else
			{
				RTSInterval = (int)value.TotalMilliseconds;
				RTSUnit = Time.Milliseconds.Name;
			}
		}
	}

	[HashContributor]
	[Metadata("Fixed Start")]
	public DateTime FixedStartTime { get; set; } = NullTimestamp;

	[HashContributor]
	[Metadata("Use Fixed Start")]
	public bool UseFixedStartTime { get; set; }

	public static TimeSeriesDefinition Regular(Time timeUnits, int timeInterval, DateTime? fixedStartTime = null)
	{
		return Regular(timeUnits.Name, timeInterval, fixedStartTime);
	}

	public static TimeSeriesDefinition Regular(string timeUnits, int timeInterval, DateTime? fixedStartTime = null)
	{
		TimeSeriesDefinition timeSeriesDefinition = new TimeSeriesDefinition();
		if (fixedStartTime.HasValue)
		{
			timeSeriesDefinition.FixedStartTime = fixedStartTime.Value;
		}
		timeSeriesDefinition.TimeSeriesType = TimeSeriesType.Regular;
		timeSeriesDefinition.RTSInterval = timeInterval;
		timeSeriesDefinition.RTSUnit = timeUnits;
		return timeSeriesDefinition;
	}

	public static TimeSeriesDefinition Irregular(DateTime[] dts)
	{
		return new TimeSeriesDefinition
		{
			TimeSeriesType = TimeSeriesType.Irregular,
			ExpressedTimeType = ExpressedTimeType.Absolute,
			IrregularAbsoluteTimes = dts
		};
	}

	public static TimeSeriesDefinition Irregular(double[] relativeTimeSeconds, DateTime? fixedStartTime)
	{
		TimeSeriesDefinition timeSeriesDefinition = new TimeSeriesDefinition();
		timeSeriesDefinition.TimeSeriesType = TimeSeriesType.Irregular;
		timeSeriesDefinition.ExpressedTimeType = ExpressedTimeType.Relative;
		timeSeriesDefinition.IrregularRelativeTimes = relativeTimeSeconds;
		if (fixedStartTime.HasValue)
		{
			timeSeriesDefinition.FixedStartTime = fixedStartTime.Value;
		}
		return timeSeriesDefinition;
	}

	public TimeSeriesDefinitionBase ToNewTimeSeries()
	{
		if (TimeSeriesType == TimeSeriesType.Regular)
		{
			return new RegularTimeSeriesDefinition(RTSTimeSpan);
		}
		if (TimeSeriesType == TimeSeriesType.Irregular)
		{
			if (ExpressedTimeType == ExpressedTimeType.Absolute)
			{
				return new IrregularAbsoluteTimeSeriesDefinition(IrregularAbsoluteTimes);
			}
			if (ExpressedTimeType == ExpressedTimeType.Relative)
			{
				return new IrregularRelativeTimeSeriesDefinition(IrregularRelativeTimes.Select((double sec) => TimeSpan.FromSeconds(sec)).ToArray());
			}
			throw new NotImplementedException();
		}
		throw new NotImplementedException();
	}

	public bool TrySetRTSTimespan(string stringValue)
	{
		if (Time.TryParseTimestep(stringValue, out var ts))
		{
			RTSTimeSpan = ts;
			return true;
		}
		return false;
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

	public OperationResult IsValid(int nOrdinates)
	{
		if (TimeSeriesType == TimeSeriesType.Irregular)
		{
			if (ExpressedTimeType == ExpressedTimeType.Absolute)
			{
				if (IrregularAbsoluteTimes == null || IrregularAbsoluteTimes.Length != nOrdinates)
				{
					return OperationResult.Fail("Incorrect Irregular DateTime count.");
				}
				return true;
			}
			if (IrregularRelativeTimes == null || IrregularRelativeTimes.Length != nOrdinates)
			{
				return OperationResult.Fail("Incorrect Irregular DateTime count.");
			}
			return true;
		}
		if (TimeSeriesType == TimeSeriesType.Regular)
		{
			return true;
		}
		return OperationResult.Fail("TimeSeriesType not recognized.");
	}

	public DateTime[] Evaluate(int nOrdinates, DateTime? inheritedStartTime = null)
	{
		if (TimeSeriesType == TimeSeriesType.Irregular)
		{
			if (ExpressedTimeType == ExpressedTimeType.Absolute)
			{
				if (IrregularAbsoluteTimes.Length != nOrdinates)
				{
					throw new Exception("# Ordinates doesn't match given DT array.");
				}
				return IrregularAbsoluteTimes.ToArray();
			}
			if (IrregularRelativeTimes.Length != nOrdinates)
			{
				throw new Exception("# Ordinates doesn't match given DT array.");
			}
			if (FixedStartTime == NullTimestamp)
			{
				if (!inheritedStartTime.HasValue)
				{
					throw new Exception("No valid DateTime offset found.");
				}
				return IrregularRelativeTimes.Select((double sec) => SafeAddSeconds(inheritedStartTime.Value, sec)).ToArray();
			}
			return IrregularRelativeTimes.Select((double sec) => SafeAddSeconds(FixedStartTime, sec)).ToArray();
		}
		if (TimeSeriesType == TimeSeriesType.Regular)
		{
			DateTime dateTime;
			if (FixedStartTime == NullTimestamp)
			{
				if (!inheritedStartTime.HasValue)
				{
					throw new Exception("No valid DateTime offset found.");
				}
				dateTime = inheritedStartTime.Value;
			}
			else
			{
				dateTime = FixedStartTime;
			}
			TimeSpan rTSTimeSpan = RTSTimeSpan;
			DateTime[] dateTimes = new DateTime[nOrdinates];
			for (int intervalIndex = 0; intervalIndex < dateTimes.Length; intervalIndex++)
			{
				dateTimes[intervalIndex] = dateTime.Add(rTSTimeSpan * intervalIndex);
			}
			return dateTimes;
		}
		throw new NotImplementedException();
	}

	public DateTime EvaluateAtIndex(int rowIndex, int nOrdinates, DateTime? inheritedStartTime = null)
	{
		if (TimeSeriesType == TimeSeriesType.Irregular)
		{
			if (ExpressedTimeType == ExpressedTimeType.Absolute)
			{
				if (IrregularAbsoluteTimes.Length != nOrdinates)
				{
					throw new Exception("# Ordinates doesn't match given DT array.");
				}
				return IrregularAbsoluteTimes[rowIndex];
			}
			if (IrregularRelativeTimes.Length != nOrdinates)
			{
				throw new Exception("# Ordinates doesn't match given DT array.");
			}
			if (FixedStartTime == NullTimestamp)
			{
				if (!inheritedStartTime.HasValue)
				{
					throw new Exception("No valid DateTime offset found.");
				}
				return SafeAddSeconds(inheritedStartTime.Value, IrregularRelativeTimes[rowIndex]);
			}
			return SafeAddSeconds(FixedStartTime, IrregularRelativeTimes[rowIndex]);
		}
		if (TimeSeriesType == TimeSeriesType.Regular)
		{
			return ((!(FixedStartTime == NullTimestamp) && !inheritedStartTime.HasValue) ? FixedStartTime : inheritedStartTime.GetValueOrDefault()).Add(RTSTimeSpan * rowIndex);
		}
		throw new NotImplementedException();
	}

	public double[] EvaluateRelativeTime(int nOrdinates, DateTime inheritedStartTime)
	{
		DateTime[] evaluate = Evaluate(nOrdinates, inheritedStartTime);
		double[] relativeTime = new double[nOrdinates];
		for (int i = 0; i < relativeTime.Length; i++)
		{
			relativeTime[i] = (evaluate[i] - inheritedStartTime).TotalSeconds;
		}
		return relativeTime;
	}

	public bool CoversTimeWindow(DateTime startTime, TimeSpan duration, int nOrdinates, DateTime inheritedStartTime, ProfileInterpolationMethod interp = ProfileInterpolationMethod.Linear)
	{
		if (TimeSeriesType == TimeSeriesType.Regular)
		{
			DateTime dateTime = (UseFixedStartTime ? FixedStartTime : inheritedStartTime);
			DateTime dateTime2;
			DateTime dateTime3;
			switch (interp)
			{
			case ProfileInterpolationMethod.ApplyPointToPreviousSegment:
				dateTime2 = dateTime - RTSTimeSpan;
				dateTime3 = dateTime + RTSTimeSpan * nOrdinates;
				break;
			case ProfileInterpolationMethod.ApplyPointToNextSegment:
				dateTime2 = dateTime;
				dateTime3 = dateTime + RTSTimeSpan * (nOrdinates + 1);
				break;
			case ProfileInterpolationMethod.Linear:
				dateTime2 = dateTime;
				dateTime3 = dateTime + RTSTimeSpan * nOrdinates;
				break;
			default:
				throw new NotImplementedException();
			}
			DateTime dateTime4 = startTime + duration;
			if (dateTime2 > startTime)
			{
				return false;
			}
			if (dateTime3 < dateTime4)
			{
				return false;
			}
			return true;
		}
		if (ExpressedTimeType == ExpressedTimeType.Absolute)
		{
			if (IrregularAbsoluteTimes == null)
			{
				return false;
			}
			if (IrregularAbsoluteTimes.First() <= startTime)
			{
				return IrregularAbsoluteTimes.Last() >= startTime + duration;
			}
			return false;
		}
		if (IrregularRelativeTimes == null)
		{
			return false;
		}
		if (IrregularRelativeTimes.First() <= 0.0)
		{
			return TimeSpan.FromSeconds(IrregularRelativeTimes.Last()) >= duration;
		}
		return false;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
