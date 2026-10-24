using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RasMapperLib.ArrivalTime;

[ComVisible(false)]
public class MultiLocationTimeSeriesExtrema : List<TimeSeriesExtrema>
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__1_002D0
	{
		public float[] minWSPerTimstep;

		public float[] maxWSPerTimstep;

		public _Closure_0024__1_002D0(_Closure_0024__1_002D0 other)
		{
			if (other != null)
			{
				minWSPerTimstep = other.minWSPerTimstep;
				maxWSPerTimstep = other.maxWSPerTimstep;
			}
		}

		[SpecialName]
		internal float _Lambda_0024__3(int startTime, int stopTime)
		{
			float maxValue = float.MaxValue;
			for (int i = startTime; i <= stopTime; i = checked(i + 1))
			{
				maxValue = Math.Min(maxValue, minWSPerTimstep[i]);
			}
			return maxValue;
		}

		[SpecialName]
		internal float _Lambda_0024__4(int startTime, int stopTime)
		{
			float minValue = float.MinValue;
			for (int i = startTime; i <= stopTime; i = checked(i + 1))
			{
				minValue = Math.Max(minValue, maxWSPerTimstep[i]);
			}
			return minValue;
		}
	}

	public WindowSeries Combine()
	{
		_Closure_0024__1_002D0 capturedLocals = default;
		_Closure_0024__1_002D0 capturedLocals2 = new _Closure_0024__1_002D0(capturedLocals);
		if (Count == 0)
		{
			return null;
		}
		if (Count == 1)
		{
			return base[0].ToWindowSeries();
		}
		Validate();
		int time = base[0].Last().LocalExtrema.Time;
		checked
		{
			capturedLocals2.minWSPerTimstep = new float[time + 1];
			capturedLocals2.maxWSPerTimstep = new float[time + 1];
			Slope[] slopes = new Slope[time - 1 + 1];
			int time3 = time;
			for (int i = 0; i <= time3; i++)
			{
				capturedLocals2.minWSPerTimstep[i] = float.MaxValue;
				capturedLocals2.maxWSPerTimstep[i] = float.MinValue;
			}
			int count = Count - 1;
			for (int j = 0; j <= count; j++)
			{
				int count2 = base[j].Count - 2;
				for (int k = 0; k <= count2; k++)
				{
					LocalExtremaValue localExtremaValue = base[j][k];
					LocalExtremaValue localExtremaValue2 = base[j][k + 1];
					int time2 = localExtremaValue.LocalExtrema.Time;
					int count3 = localExtremaValue2.LocalExtrema.Time - 1;
					for (int l = time2; l <= count3; l++)
					{
						Slope slope = slopes[l];
						capturedLocals2.minWSPerTimstep[l] = Math.Min(capturedLocals2.minWSPerTimstep[l], localExtremaValue.Value);
						capturedLocals2.maxWSPerTimstep[l] = Math.Max(capturedLocals2.maxWSPerTimstep[l], localExtremaValue.Value);
						capturedLocals2.minWSPerTimstep[l] = Math.Min(capturedLocals2.minWSPerTimstep[l], localExtremaValue2.Value);
						capturedLocals2.maxWSPerTimstep[l] = Math.Max(capturedLocals2.maxWSPerTimstep[l], localExtremaValue2.Value);
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
			List<ListExtensionMethods.Streak> mutuallyExclusiveStreaks = ListExtensionMethods.MutuallyExclusiveStreaks(slopes, [SpecialName] (Slope sl) => sl == Slope.Increasing);
			List<ListExtensionMethods.Streak> mutuallyExclusiveStreaksList = ListExtensionMethods.MutuallyExclusiveStreaks(slopes, [SpecialName] (Slope sl) => sl == Slope.Decreasing);
			List<ListExtensionMethods.Streak> mutuallyExclusiveStreaks2 = ListExtensionMethods.MutuallyExclusiveStreaks(slopes, [SpecialName] (Slope sl) => sl == Slope.Indeterminate);
			Func<int, int, float> func = [SpecialName] (int startTime, int stopTime) =>
			{
				float maxValue = float.MaxValue;
				for (int m = startTime; m <= stopTime; m++)
				{
					maxValue = Math.Min(maxValue, capturedLocals2.minWSPerTimstep[m]);
				}
				return maxValue;
			};
			Func<int, int, float> func2 = [SpecialName] (int startTime, int stopTime) =>
			{
				float minValue = float.MinValue;
				for (int m = startTime; m <= stopTime; m++)
				{
					minValue = Math.Max(minValue, capturedLocals2.maxWSPerTimstep[m]);
				}
				return minValue;
			};
			WindowSeries windowSeries = new WindowSeries();
			foreach (ListExtensionMethods.Streak mutuallyExclusiveStreak in mutuallyExclusiveStreaks)
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
			foreach (ListExtensionMethods.Streak mutuallyExclusiveStreakList in mutuallyExclusiveStreaksList)
			{
				int startIndex2 = mutuallyExclusiveStreakList.StartIndex;
				int decreasingIds = mutuallyExclusiveStreakList.StopIndex + 1;
				if (mutuallyExclusiveStreakList.StreakType != 0)
				{
					float min2 = func(startIndex2, decreasingIds);
					float max2 = func2(startIndex2, decreasingIds);
					windowSeries.Add(new Window(startIndex2, decreasingIds, min2, max2, Slope.Decreasing));
				}
			}
			foreach (ListExtensionMethods.Streak mutuallyExclusiveStreak2 in mutuallyExclusiveStreaks2)
			{
				int startIndex3 = mutuallyExclusiveStreak2.StartIndex;
				int indeterminateIds = mutuallyExclusiveStreak2.StopIndex + 1;
				if (mutuallyExclusiveStreak2.StreakType != 0)
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

	private void Validate()
	{
	}
}
