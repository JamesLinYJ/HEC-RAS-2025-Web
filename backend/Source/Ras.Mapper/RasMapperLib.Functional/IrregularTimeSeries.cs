using System;
using System.Collections.Generic;
using System.Linq;
using H5Assist.VB;

namespace RasMapperLib.Functional;

public class IrregularTimeSeries : DiscreteTimeSeries
{
	public const string sH5TypeName = "Irregular Time Series";

	private List<DateTime> _dateTimes;

	private List<double> _data;

	public override int Count => _data.Count;

	// C# has no syntax for parameterized property 'Value'.
	public override double get_Value(int idx)
	{
		return _data[idx];
	}

	public override void set_Value(int idx, double Value)
	{
		_data[idx] = Value;
	}

	// C# has no syntax for parameterized property 'DateTime'.
	public override DateTime get_DateTime(int idx)
	{
		return _dateTimes[idx];
	}

	public override DateTime StartTime => _dateTimes.FirstOrDefault();

	public override DateTime EndTime => _dateTimes.LastOrDefault();

	public override TimeSpan Duration => EndTime - StartTime;

	public IrregularTimeSeries(IList<double> data, IList<DateTime> dateTimes)
		: this(data, dateTimes, "", "")
	{
	}

	public IrregularTimeSeries(IList<double> data, IList<DateTime> dateTimes, string variable, string units)
		: base(variable, units)
	{
		_data = data.ToList();
		_dateTimes = dateTimes.ToList();
	}

	public void Add(double v, DateTime dt)
	{
		_data.Add(v);
		_dateTimes.Add(dt);
	}

	public override List<double> CopyData()
	{
		return _data.ToList();
	}

	public override List<DateTime> CopyDates()
	{
		return _dateTimes.ToList();
	}

	public override double Sample(DateTime dt, bool canExtrapolate = false)
	{
		if (Count == 0)
		{
			return double.NaN;
		}
		if (DateTime.Compare(dt, StartTime) == 0)
		{
			return _data.First();
		}
		if (DateTime.Compare(dt, EndTime) == 0)
		{
			return _data.Last();
		}
		if (DateTime.Compare(dt, StartTime) < 0)
		{
			if (canExtrapolate)
			{
				return _data.First();
			}
			return double.NaN;
		}
		if (DateTime.Compare(dt, EndTime) > 0)
		{
			if (canExtrapolate)
			{
				return _data.Last();
			}
			return double.NaN;
		}
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DateTime dateTime = _dateTimes[i];
				if (DateTime.Compare(dt, dateTime) < 0)
				{
					DateTime dateTime2 = _dateTimes[i - 1];
					double totalSecondsOverTotalSeconds = (dt - dateTime2).TotalSeconds / (dateTime - dateTime2).TotalSeconds;
					return totalSecondsOverTotalSeconds * _data[i] + (1.0 - totalSecondsOverTotalSeconds) * _data[i - 1];
				}
			}
			return double.NaN;
		}
	}

	protected override void SaveInternal(H5Writer hw, string groupName)
	{
		throw new NotImplementedException();
	}
}
