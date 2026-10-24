using System;

namespace Utility;

public class DateTimeRange
{
	public static readonly DateTime NullDateTime = DateTime.MinValue;

	public DateTime Start { get; set; } = NullDateTime;

	public DateTime End { get; set; } = NullDateTime;

	public TimeSpan Range => End - Start;

	public bool IsSet
	{
		get
		{
			if (!(Start == NullDateTime))
			{
				return !(End == NullDateTime);
			}
			return false;
		}
	}

	public bool IsInstant
	{
		get
		{
			if (Start == End)
			{
				return Start != NullDateTime;
			}
			return false;
		}
	}

	public bool IsValid
	{
		get
		{
			if (IsSet)
			{
				return Start <= End;
			}
			return false;
		}
	}

	public DateTimeRange()
	{
	}

	public DateTimeRange(DateTime startDt, DateTime endDt)
	{
		Start = startDt;
		End = endDt;
	}
}
