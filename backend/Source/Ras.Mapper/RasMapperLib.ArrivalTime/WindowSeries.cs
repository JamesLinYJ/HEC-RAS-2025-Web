using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RasMapperLib.ArrivalTime;

[ComVisible(false)]
public class WindowSeries
{
	public List<Window> Windows;

	public int Count
	{
		get
		{
			if (Windows == null)
			{
				return 0;
			}
			return Windows.Count;
		}
	}

	public Window this[int idx] => Windows[idx];

	public WindowSeries()
	{
		Windows = new List<Window>();
	}

	public WindowSeries(List<Window> windows)
	{
		Windows = windows;
	}

	public void Add(Window window)
	{
		Windows.Add(window);
	}

	public Window Last()
	{
		return Windows.Last();
	}

	public void Sort()
	{
		Windows = Windows.OrderBy([SpecialName] (Window s) => s.StartTime).ToList();
	}

	public void FillWithIndeterminateRegions(int lastTime, Func<int, int, float> GetAbsMinWS, Func<int, int, float> GetAbsMaxWS)
	{
		Sort();
		if (Count == 0)
		{
			Add(new Window(0, lastTime, GetAbsMinWS(0, lastTime), GetAbsMaxWS(0, lastTime), Slope.Indeterminate));
			return;
		}
		checked
		{
			int countOffset = Count - 1;
			int count = countOffset - 1;
			for (int i = 0; i <= count; i++)
			{
				int stopTime = this[i].StopTime;
				int startTime = this[i + 1].StartTime;
				if (stopTime != startTime)
				{
					float min = GetAbsMinWS(stopTime, startTime);
					float max = GetAbsMaxWS(stopTime, startTime);
					Add(new Window(stopTime, startTime, min, max, Slope.Indeterminate));
				}
			}
			if (this[countOffset].StopTime != lastTime)
			{
				int stopTime2 = this[countOffset].StopTime;
				float min2 = GetAbsMinWS(stopTime2, lastTime);
				float max2 = GetAbsMaxWS(stopTime2, lastTime);
				Add(new Window(stopTime2, lastTime, min2, max2, Slope.Indeterminate));
			}
			int startTime2 = this[0].StartTime;
			if (startTime2 != 0)
			{
				float min3 = GetAbsMinWS(0, startTime2);
				float max3 = GetAbsMaxWS(0, startTime2);
				Add(new Window(0, startTime2, min3, max3, Slope.Indeterminate));
			}
			Sort();
		}
	}
}
