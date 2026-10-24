using System;
using Utility.Units;

namespace Ras.Layers;

public class TimestepCollection
{
	private readonly DateTime[] _times;

	private readonly double[] _deltaFromStart;

	private readonly double _rtsDelta;

	private readonly Func<DateTime> _getStart;

	private readonly DateTime _start;

	private readonly Time _units;

	public int Count { get; }

	public DateTime Start
	{
		get
		{
			if (_times != null)
			{
				return _times[0];
			}
			if (_getStart != null)
			{
				return _getStart();
			}
			return _start;
		}
	}

	public TimestepCollection(DateTime[] dts)
	{
		_times = dts;
		Count = dts.Length;
	}

	public TimestepCollection(DateTime start, double[] deltaTime, Time units)
	{
		_start = start;
		_deltaFromStart = deltaTime;
		_units = units;
		Count = deltaTime.Length;
	}

	public TimestepCollection(Func<DateTime> getStart, double[] deltaTime, Time units)
	{
		_getStart = getStart;
		_deltaFromStart = deltaTime;
		_units = units;
		Count = deltaTime.Length;
	}

	public TimestepCollection(DateTime start, double deltaTime, int timestepCount, Time units)
	{
		_start = start;
		Count = timestepCount;
		_rtsDelta = deltaTime;
		_units = units;
	}

	public TimestepCollection(Func<DateTime> getStart, double deltaTime, int timestepCount, Time units)
	{
		_getStart = getStart;
		Count = timestepCount;
		_rtsDelta = deltaTime;
		_units = units;
	}

	public DateTime Timestep(int idx)
	{
		if (_times != null)
		{
			return _times[idx];
		}
		if (_deltaFromStart != null)
		{
			return Start + _units.ToTimespan(_deltaFromStart[idx]);
		}
		return Start + _units.ToTimespan((double)idx * _rtsDelta);
	}
}
