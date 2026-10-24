using System;
using System.Diagnostics;
using Utility.Hashing;
using Utility.Logging;

namespace Geospatial.PairedData;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class RegularTimeSeriesDefinition : TimeSeriesDefinitionBase
{
	[HashContributor]
	public TimeSpan Interval { get; set; }

	[HashContributor]
	public DateTime FixedStartTime { get; set; }

	public override int Count => -1;

	public bool UseFixedStartTime => FixedStartTime != TimeSeriesDefinitionBase.NullTimestamp;

	public RegularTimeSeriesDefinition(TimeSpan ts, DateTime? fixedStartTime = null)
	{
		Interval = ts;
		FixedStartTime = fixedStartTime ?? TimeSeriesDefinitionBase.NullTimestamp;
	}

	public override void GetRawTimeValues(Memory<long> times)
	{
		Span<long> timesSpan = times.Span;
		if (FixedStartTime == TimeSeriesDefinitionBase.NullTimestamp)
		{
			long ticks = Interval.Ticks;
			long ticksSum = 0L;
			for (int i = 0; i < timesSpan.Length; i++)
			{
				timesSpan[i] = ticksSum;
				ticksSum += ticks;
			}
		}
		else
		{
			long ticks2 = Interval.Ticks;
			long ticks2Sum = 0L;
			for (int j = 0; j < timesSpan.Length; j++)
			{
				timesSpan[j] = ticks2Sum;
				ticks2Sum += ticks2;
			}
		}
	}

	public override DateTime[] GetAbsoluteTimes(int nOrdinates, DateTime startTime)
	{
		DateTime dateTime = ((!UseFixedStartTime) ? startTime : FixedStartTime);
		TimeSpan interval = Interval;
		DateTime[] absoluteTimes = new DateTime[nOrdinates];
		for (int i = 0; i < absoluteTimes.Length; i++)
		{
			absoluteTimes[i] = dateTime.Add(interval * i);
		}
		return absoluteTimes;
	}

	public override DateTime TimeFor(DateTime injectedStartTime, int index)
	{
		if (UseFixedStartTime)
		{
			return FixedStartTime + Interval * index;
		}
		return injectedStartTime + Interval * index;
	}

	public OperationResult DataEquals(RegularTimeSeriesDefinition other)
	{
		OperationResult operationResult = true;
		if (Interval != other.Interval)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("RegularTimeSeriesDefinition", "Interval", Interval, other.Interval));
		}
		if (FixedStartTime != other.FixedStartTime)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("RegularTimeSeriesDefinition", "FixedStartTime", FixedStartTime, other.FixedStartTime));
		}
		return operationResult;
	}

	private string GetDebuggerDisplay()
	{
		return ToString();
	}

	public override bool CoversTimeWindow(DateTime startTime, TimeSpan duration, int nOrdinates, DateTime inheritedStartTime, ProfileInterpolationMethod interp = ProfileInterpolationMethod.Linear)
	{
		DateTime dateTime = (UseFixedStartTime ? FixedStartTime : inheritedStartTime);
		DateTime dateTime2;
		DateTime dateTime3;
		switch (interp)
		{
		case ProfileInterpolationMethod.ApplyPointToPreviousSegment:
			dateTime2 = dateTime - Interval;
			dateTime3 = dateTime + Interval * nOrdinates;
			break;
		case ProfileInterpolationMethod.ApplyPointToNextSegment:
			dateTime2 = dateTime;
			dateTime3 = dateTime + Interval * (nOrdinates + 1);
			break;
		case ProfileInterpolationMethod.Linear:
			dateTime2 = dateTime;
			dateTime3 = dateTime + Interval * nOrdinates;
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
}
