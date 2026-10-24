using System;
using System.Collections.Generic;
using System.Linq;

namespace Ras.Mapping.ArrivalTime;

public class MultiLocationTimeSeriesExtrema : List<TimeSeriesExtrema>
{
	public WindowSeries Combine()
	{
		if (Count == 0)
		{
			return null;
		}
		if (Count == 1)
		{
			return base[0].ToWindowSeries();
		}
		int time = base[0].Last().LocalExtrema.Time;
		float[] minWSPerTimstep = new float[time + 1];
		float[] maxWSPerTimstep = new float[time + 1];
		Slope[] slopes = new Slope[time];
		for (int i = 0; i <= time; i++)
		{
			minWSPerTimstep[i] = float.MaxValue;
			maxWSPerTimstep[i] = float.MinValue;
		}
		for (int j = 0; j <= Count - 1; j++)
		{
			for (int k = 0; k <= base[j].Count - 2; k++)
			{
				LocalExtremaValue localExtremaValue = base[j][k];
				LocalExtremaValue localExtremaValue2 = base[j][k + 1];
				for (int l = localExtremaValue.LocalExtrema.Time; l <= localExtremaValue2.LocalExtrema.Time - 1; l++)
				{
					Slope slope = slopes[l];
					minWSPerTimstep[l] = Math.Min(minWSPerTimstep[l], localExtremaValue.Value);
					maxWSPerTimstep[l] = Math.Max(maxWSPerTimstep[l], localExtremaValue.Value);
					minWSPerTimstep[l] = Math.Min(minWSPerTimstep[l], localExtremaValue2.Value);
					maxWSPerTimstep[l] = Math.Max(maxWSPerTimstep[l], localExtremaValue2.Value);
					if (localExtremaValue.LocalExtrema.Extrema == ExtremaType.Minima)
					{
						if (j == 0 || slope == Slope.Increasing)
						{
							slopes[l] = Slope.Increasing;
						}
						else
						{
							slopes[l] = Slope.Indeterminate;
						}
					}
					else if (j == 0 || slope == Slope.Decreasing)
					{
						slopes[l] = Slope.Decreasing;
					}
					else
					{
						slopes[l] = Slope.Indeterminate;
					}
				}
			}
		}
		List<Streak> mutuallyExclusiveStreaks = slopes.MutuallyExclusiveStreaks((Slope sl) => sl == Slope.Increasing);
		List<Streak> mutuallyExclusiveStreaksList = slopes.MutuallyExclusiveStreaks((Slope sl) => sl == Slope.Decreasing);
		List<Streak> mutuallyExclusiveStreaks2 = slopes.MutuallyExclusiveStreaks((Slope sl) => sl == Slope.Indeterminate);
		Func<int, int, float> func = (int startTime, int stopTime) =>
		{
			float maxValue = float.MaxValue;
			for (int m = startTime; m <= stopTime; m++)
			{
				maxValue = Math.Min(maxValue, minWSPerTimstep[m]);
			}
			return maxValue;
		};
		Func<int, int, float> func2 = (int startTime, int stopTime) =>
		{
			float minValue = float.MinValue;
			for (int m = startTime; m <= stopTime; m++)
			{
				minValue = Math.Max(minValue, maxWSPerTimstep[m]);
			}
			return minValue;
		};
		WindowSeries windowSeries = new WindowSeries();
		foreach (Streak mutuallyExclusiveStreak in mutuallyExclusiveStreaks)
		{
			int startIndex = mutuallyExclusiveStreak.StartIndex;
			int increasingIds = mutuallyExclusiveStreak.StopIndex + 1;
			if (mutuallyExclusiveStreak.StreakType != 0)
			{
				float min = func(startIndex, increasingIds);
				float max = func2(startIndex, increasingIds);
				windowSeries.Add(new Window(startIndex, increasingIds, min, max, Slope.Increasing));
			}
		}
		foreach (Streak mutuallyExclusiveStreak2 in mutuallyExclusiveStreaksList)
		{
			int startIndex2 = mutuallyExclusiveStreak2.StartIndex;
			int decreasingIds = mutuallyExclusiveStreak2.StopIndex + 1;
			if (mutuallyExclusiveStreak2.StreakType != 0)
			{
				float min2 = func(startIndex2, decreasingIds);
				float max2 = func2(startIndex2, decreasingIds);
				windowSeries.Add(new Window(startIndex2, decreasingIds, min2, max2, Slope.Decreasing));
			}
		}
		foreach (Streak mutuallyExclusiveStreak3 in mutuallyExclusiveStreaks2)
		{
			int startIndex3 = mutuallyExclusiveStreak3.StartIndex;
			int indeterminateIds = mutuallyExclusiveStreak3.StopIndex + 1;
			if (mutuallyExclusiveStreak3.StreakType != 0)
			{
				float min3 = func(startIndex3, indeterminateIds);
				float max3 = func2(startIndex3, indeterminateIds);
				windowSeries.Add(new Window(startIndex3, indeterminateIds, min3, max3, Slope.Indeterminate));
			}
		}
		windowSeries.Sort();
		return windowSeries;
	}
}
