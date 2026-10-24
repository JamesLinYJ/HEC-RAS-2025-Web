using System;
using Utility.Hashing;
using Utility.Logging;

namespace Geospatial.PairedData;

public abstract class TimeSeriesDefinitionBase : IDataHash
{
	public static readonly DateTime NullTimestamp = DateTime.MinValue;

	[HashContributor]
	public LineInterpolationMethod LineInterpolationMethod { get; set; }

	public abstract int Count { get; }

	public abstract void GetRawTimeValues(Memory<long> times);

	public abstract DateTime[] GetAbsoluteTimes(int nOrdinates, DateTime startTime);

	public double[] GetRelativeTimeSeconds(int nOrdinates, DateTime inheritedStartTime)
	{
		DateTime[] absoluteTimes = GetAbsoluteTimes(nOrdinates, inheritedStartTime);
		double[] relativeTimeSeconds = new double[nOrdinates];
		for (int i = 0; i < relativeTimeSeconds.Length; i++)
		{
			relativeTimeSeconds[i] = (absoluteTimes[i] - inheritedStartTime).TotalSeconds;
		}
		return relativeTimeSeconds;
	}

	public DateTime StartTime(DateTime injectedStartTime)
	{
		return TimeFor(injectedStartTime, 0);
	}

	public abstract DateTime TimeFor(DateTime injectedStartTime, int index);

	public abstract bool CoversTimeWindow(DateTime startTime, TimeSpan duration, int nOrdinates, DateTime floatingStartTime, ProfileInterpolationMethod interp = ProfileInterpolationMethod.Linear);

	public void Switch(Action<RegularTimeSeriesDefinition> ifRTS, Action<IrregularAbsoluteTimeSeriesDefinition> ifIRTSAbs, Action<IrregularRelativeTimeSeriesDefinition> ifIRTSRel)
	{
		if (this is RegularTimeSeriesDefinition regularTimeSeriesDefinition)
		{
			ifRTS(regularTimeSeriesDefinition);
			return;
		}
		if (this is IrregularAbsoluteTimeSeriesDefinition irregularAbsoluteTimeSeriesDefinition)
		{
			ifIRTSAbs(irregularAbsoluteTimeSeriesDefinition);
			return;
		}
		if (this is IrregularRelativeTimeSeriesDefinition irregularRelativeTimeSeriesDefinition)
		{
			ifIRTSRel(irregularRelativeTimeSeriesDefinition);
			return;
		}
		throw new NotImplementedException("TimeSeries type not recognized.");
	}

	public T Switch<T>(Func<RegularTimeSeriesDefinition, T> ifRTS, Func<IrregularAbsoluteTimeSeriesDefinition, T> ifIRTSAbs, Func<IrregularRelativeTimeSeriesDefinition, T> ifIRTSRel)
	{
		if (this is RegularTimeSeriesDefinition regularTimeSeriesDefinition)
		{
			return ifRTS(regularTimeSeriesDefinition);
		}
		if (this is IrregularAbsoluteTimeSeriesDefinition irregularAbsoluteTimeSeriesDefinition)
		{
			return ifIRTSAbs(irregularAbsoluteTimeSeriesDefinition);
		}
		if (this is IrregularRelativeTimeSeriesDefinition irregularRelativeTimeSeriesDefinition)
		{
			return ifIRTSRel(irregularRelativeTimeSeriesDefinition);
		}
		throw new NotImplementedException("TimeSeries type not recognized.");
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public OperationResult DataEquals(TimeSeriesDefinitionBase other)
	{
		OperationResult or = true;
		if (GetDataHash() != other.GetDataHash())
		{
			or.AddFailure("TimeSeriesDefinitionBase hash mismatch.");
		}
		if (LineInterpolationMethod != other.LineInterpolationMethod)
		{
			or.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("TimeSeriesDefinitionBase", "LineInterpolationMethod", LineInterpolationMethod, other.LineInterpolationMethod));
		}
		if (GetType() != other.GetType())
		{
			or.AddFailure("TimeSeriesDefinitionBase concrete type mismatch " + GetType().Name + " | " + other.GetType().Name);
		}
		else
		{
			Switch((RegularTimeSeriesDefinition rtsd) =>
			{
				OperationResult operationResult = rtsd.DataEquals(other as RegularTimeSeriesDefinition);
				if (!operationResult)
				{
					or.AddFailure(operationResult);
				}
			}, (IrregularAbsoluteTimeSeriesDefinition irtsAbs) =>
			{
			}, (IrregularRelativeTimeSeriesDefinition irtsRel) =>
			{
			});
		}
		return or;
	}
}
