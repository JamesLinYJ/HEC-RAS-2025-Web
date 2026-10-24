using System;
using System.Collections.Generic;

namespace Ras.Mapping.ArrivalTime;

public static class StreakListExtensions
{
	public static List<Streak> MutuallyExclusiveStreaks<T>(this IList<T> input, params Func<T, bool>[] cases)
	{
		List<Streak> streaks = new List<Streak>();
		if (input.Count == 0)
		{
			return streaks;
		}
		if (cases.Length == 0)
		{
			streaks.Add(new Streak(0, input.Count - 1, 0));
			return streaks;
		}
		Func<T, int> func = (T localValue) =>
		{
			for (int i = 0; i < cases.Length; i++)
			{
				if (cases[i](localValue))
				{
					return i + 1;
				}
			}
			return 0;
		};
		int func2 = func(input[0]);
		int startIdx = 0;
		for (int startIdx2 = 0; startIdx2 < input.Count; startIdx2++)
		{
			int func3 = func(input[startIdx2]);
			if (func3 != func2)
			{
				streaks.Add(new Streak(startIdx, startIdx2 - 1, func2));
				startIdx = startIdx2;
				func2 = func3;
			}
		}
		streaks.Add(new Streak(startIdx, input.Count - 1, func2));
		return streaks;
	}
}
