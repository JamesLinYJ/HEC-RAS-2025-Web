using System;
using System.Collections.Generic;

namespace RasMapperLib.Functional;

public abstract class DiscreteTimeSeries : TimeSeries
{
	public abstract int Count { get; }

	// C# has no syntax for parameterized property 'Value'.
	public abstract double get_Value(int idx);

	public abstract void set_Value(int idx, double Value);

	// C# has no syntax for parameterized property 'DateTime'.
	public abstract DateTime get_DateTime(int idx);

	public abstract TimeSpan Duration { get; }

	public abstract List<double> CopyData();

	public abstract List<DateTime> CopyDates();

	public DiscreteTimeSeries()
	{
	}

	public DiscreteTimeSeries(string variable, string units)
		: base(variable, units)
	{
	}
}
