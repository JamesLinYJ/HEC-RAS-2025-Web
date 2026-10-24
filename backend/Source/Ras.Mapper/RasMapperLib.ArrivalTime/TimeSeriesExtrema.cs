using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RasMapperLib.ArrivalTime;

[ComVisible(false)]
public class TimeSeriesExtrema : List<LocalExtremaValue>
{
	public TimeSeriesExtrema(int ct)
		: base(ct)
	{
	}

	public void AddRange(List<LocalExtrema> extrema, List<float> values)
	{
		if (extrema == null)
		{
			throw new ArgumentNullException("extrema");
		}
		if (values == null)
		{
			throw new ArgumentNullException("values");
		}
		if (extrema.Count != values.Count)
		{
			throw new ArgumentException("Extrema and values must have the same number of values");
		}
		checked
		{
			int count = extrema.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Add(new LocalExtremaValue(extrema[i], values[i]));
			}
		}
	}

	public WindowSeries ToWindowSeries()
	{
		WindowSeries windowSeries = new WindowSeries();
		checked
		{
			int count = Count - 2;
			for (int i = 0; i <= count; i++)
			{
				LocalExtremaValue localExtremaValue = base[i];
				LocalExtremaValue localExtremaValue2 = base[i + 1];
				if (localExtremaValue2.Value >= localExtremaValue.Value)
				{
					windowSeries.Add(new Window(localExtremaValue.LocalExtrema.Time, localExtremaValue2.LocalExtrema.Time, localExtremaValue.Value, localExtremaValue2.Value, Slope.Increasing));
				}
				else
				{
					windowSeries.Add(new Window(localExtremaValue.LocalExtrema.Time, localExtremaValue2.LocalExtrema.Time, localExtremaValue2.Value, localExtremaValue.Value, Slope.Decreasing));
				}
			}
			return windowSeries;
		}
	}

	public WindowSeries Combine(TimeSeriesExtrema other)
	{
		return new MultiLocationTimeSeriesExtrema { this, other }.Combine();
	}
}
