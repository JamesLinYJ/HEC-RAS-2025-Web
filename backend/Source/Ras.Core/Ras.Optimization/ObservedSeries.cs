using System;

namespace Ras.Optimization;

public sealed class ObservedSeries
{
	public double[] TimesSeconds { get; }

	public double[] Values { get; }

	public ObservedSeries(double[] timesSeconds, double[] values)
	{
		if (timesSeconds == null || values == null)
		{
			throw new ArgumentNullException((timesSeconds == null) ? "timesSeconds" : "values");
		}
		if (timesSeconds.Length != values.Length)
		{
			throw new ArgumentException("Times and values must have the same length.");
		}
		if (timesSeconds.Length == 0)
		{
			throw new ArgumentException("Observed series must not be empty.");
		}
		for (int i = 1; i < timesSeconds.Length; i++)
		{
			if (timesSeconds[i] <= timesSeconds[i - 1])
			{
				throw new ArgumentException("Observed times must be strictly increasing.");
			}
		}
		TimesSeconds = timesSeconds;
		Values = values;
	}
}
