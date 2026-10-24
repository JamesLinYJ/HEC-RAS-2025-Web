using System;
using System.Diagnostics;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class StopwatchExtensionMethods
{
	public static string PrintPrettyTimeHMS(this Stopwatch sw)
	{
		TimeSpan elapsed = sw.Elapsed;
		if (elapsed.TotalMinutes < 1.0)
		{
			return elapsed.Seconds.ToString();
		}
		if (elapsed.TotalMinutes < 60.0)
		{
			return elapsed.Minutes + ":" + elapsed.Seconds.ToString("d2");
		}
		return Math.Floor(elapsed.TotalHours) + ":" + elapsed.Minutes.ToString("d2") + ":" + elapsed.Seconds.ToString("d2");
	}
}
