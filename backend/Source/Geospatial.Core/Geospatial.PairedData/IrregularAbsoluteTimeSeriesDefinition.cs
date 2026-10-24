using System;
using System.Diagnostics;
using System.Linq;
using Utility.Hashing;

namespace Geospatial.PairedData;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class IrregularAbsoluteTimeSeriesDefinition : TimeSeriesDefinitionBase
{
	[HashContributor]
	public Memory<DateTime> Times { get; set; }

	public override int Count => Times.Length;

	public IrregularAbsoluteTimeSeriesDefinition(DateTime[] dts)
	{
		Times = dts.ToArray();
	}

	private IrregularAbsoluteTimeSeriesDefinition()
	{
	}

	public static IrregularAbsoluteTimeSeriesDefinition From(DateTime[] dts)
	{
		return new IrregularAbsoluteTimeSeriesDefinition
		{
			Times = dts
		};
	}

	public override void GetRawTimeValues(Memory<long> times)
	{
		if (times.Length != Count)
		{
			throw new ArgumentException("Array presized incorrectly.");
		}
		Span<long> timesSpan = times.Span;
		Span<DateTime> times2 = Times.Span;
		for (int i = 0; i < Count; i++)
		{
			timesSpan[i] = times2[i].Ticks;
		}
	}

	public override DateTime[] GetAbsoluteTimes(int nOrdinates, DateTime startTime)
	{
		if (Times.Length != nOrdinates)
		{
			throw new Exception("# Ordinates doesn't match given DT array.");
		}
		return Times.ToArray();
	}

	public override bool CoversTimeWindow(DateTime startTime, TimeSpan duration, int nOrdinates, DateTime inheritedStartTime, ProfileInterpolationMethod interp = ProfileInterpolationMethod.Linear)
	{
		Memory<DateTime> times = Times;
		if (times.Length < 2)
		{
			return false;
		}
		times = Times;
		Span<DateTime> timesSpan = times.Span;
		DateTime dateTime = timesSpan[0];
		DateTime dateTime2 = timesSpan[timesSpan.Length - 1];
		if (dateTime <= startTime)
		{
			return dateTime2 >= startTime + duration;
		}
		return false;
	}

	public override DateTime TimeFor(DateTime injectedStartTime, int index)
	{
		if (index >= Count)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		return Times.Span[index];
	}

	private string GetDebuggerDisplay()
	{
		return ToString();
	}
}
