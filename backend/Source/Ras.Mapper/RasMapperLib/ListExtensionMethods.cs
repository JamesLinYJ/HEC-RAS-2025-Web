using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

[StandardModule]
internal static class ListExtensionMethods
{
	[DebuggerDisplay("{ToString}")]
	public struct Streak
	{
		public int StartIndex;

		public int StopIndex;

		public int StreakType;

		public Streak(int startIdx, int stopIdx, int type)
		{
			this = default;
			StartIndex = startIdx;
			StopIndex = stopIdx;
			StreakType = type;
		}

		public override string ToString()
		{
			return StartIndex + "->" + StopIndex + ", " + StreakType.ToString().InParentheses();
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__14_002D0<_0024CLS0>
	{
		public Func<_0024CLS0, bool>[] cases;

		public _Closure_0024__14_002D0(_Closure_0024__14_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				cases = other.cases;
			}
		}

		[SpecialName]
		internal int _Lambda_0024__0(_0024CLS0 localValue)
		{
			checked
			{
				int count = cases.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (cases[i](localValue))
					{
						return i + 1;
					}
				}
				return 0;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__15_002D0<_0024CLS0>
	{
		public Func<int, bool>[] cases;

		public _Closure_0024__15_002D0(_Closure_0024__15_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				cases = other.cases;
			}
		}

		[SpecialName]
		internal int _Lambda_0024__0(int intValue)
		{
			checked
			{
				int count = cases.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (cases[i](intValue))
					{
						return i;
					}
				}
				return 0;
			}
		}
	}

	public static void RemoveRange<T>(this IList<T> input, ref T[] output, int start, int count)
	{
		int newCt = 0;
		checked
		{
			if (RemoveRangePrecheck(input, start, count, ref newCt))
			{
				if (output == null || output.Length != newCt)
				{
					output = new T[newCt - 1 + 1];
				}
				int count2 = start - 1;
				for (int i = 0; i <= count2; i++)
				{
					output[i] = input[i];
				}
				int startPlusCount = start + count;
				int count3 = input.Count - 1;
				for (int j = startPlusCount; j <= count3; j++)
				{
					output[j - count] = input[j];
				}
			}
		}
	}

	public static void RemoveRange<T>(this IList<T> input, ref List<T> output, int start, int count)
	{
		int newCt = 0;
		checked
		{
			if (RemoveRangePrecheck(input, start, count, ref newCt))
			{
				if (output == null || output.Count != newCt)
				{
					output = new List<T>(newCt);
				}
				int count2 = start - 1;
				for (int i = 0; i <= count2; i++)
				{
					output.Add(input[i]);
				}
				int startPlusCount = start + count;
				int count3 = input.Count - 1;
				for (int j = startPlusCount; j <= count3; j++)
				{
					output.Add(input[j]);
				}
			}
		}
	}

	private static bool RemoveRangePrecheck<T>(IList<T> input, int start, int count, ref int newCt)
	{
		if (count == 0)
		{
			return false;
		}
		if (start < 0 || start > input.Count)
		{
			throw new ArgumentException("start");
		}
		if (count < 0)
		{
			throw new ArgumentException("count");
		}
		newCt = checked(input.Count - count);
		if (newCt < 0)
		{
			throw new ArgumentException("count");
		}
		return true;
	}

	public static void InsertRange<T>(this IList<T> input, ref List<T> output, int start, IList<T> range)
	{
		checked
		{
			if (!range.IsNullOrEmpty())
			{
				if (start < 0 || start > input.Count)
				{
					throw new ArgumentException("start");
				}
				int countDifference = input.Count + range.Count;
				if (output == null || output.Count != countDifference)
				{
					output = new List<T>(countDifference);
				}
				int count = start - 1;
				for (int i = 0; i <= count; i++)
				{
					output.Add(input[i]);
				}
				int count2 = range.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					output.Add(range[j]);
				}
				int count3 = input.Count - 1;
				for (int k = start; k <= count3; k++)
				{
					output.Add(input[k]);
				}
			}
		}
	}

	public static void RemoveRange<T>(this List<T> items, List<T> range)
	{
		foreach (T range2 in range)
		{
			items.Remove(range2);
		}
	}

	public static void AddRangeReverse<T>(this List<T> ranges, List<T> range)
	{
		checked
		{
			for (int i = range.Count - 1; i >= 0; i += -1)
			{
				ranges.Add(range[i]);
			}
		}
	}

	public static void DisposeAll(this IEnumerable<IDisposable> iDisposables)
	{
		foreach (IDisposable iDisposable in iDisposables)
		{
			iDisposable?.Dispose();
		}
	}

	public static int IndexOf<T>(this IList<T> items, Predicate<T> firstTrue)
	{
		checked
		{
			int count = items.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (firstTrue(items[i]))
				{
					return i;
				}
			}
			return -1;
		}
	}

	public static int IndexOf<T>(this IList<T> items, T match)
	{
		checked
		{
			int count = items.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (items[i].Equals(match))
				{
					return i;
				}
			}
			return -1;
		}
	}

	public static bool CollectionEquals<T>(this IList<T> items, IList<T> other)
	{
		if (items == null || other == null)
		{
			throw new NullReferenceException();
		}
		if (items.Count != other.Count)
		{
			return false;
		}
		checked
		{
			int count = items.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!items[i].Equals(other[i]))
				{
					return false;
				}
			}
			return true;
		}
	}

	public static bool ContainsData<T>(this IList<T> items)
	{
		if (items == null || items.Count == 0)
		{
			return false;
		}
		return true;
	}

	public static List<T> SampleWithoutReplacement<T>(this IList<T> items, int intValue)
	{
		Random random = new Random();
		_ = items.Count;
		if (intValue == 0)
		{
			return new List<T>();
		}
		if (intValue > items.Count)
		{
			return null;
		}
		if (intValue == items.Count)
		{
			return new List<T>(items.ToArray());
		}
		List<int> intList = new List<int>();
		while (intList.Count != intValue)
		{
			int next = random.Next(0, items.Count);
			if (!intList.Contains(next))
			{
				intList.Add(next);
			}
		}
		List<T> itemsList = new List<T>();
		foreach (int ordinal in intList)
		{
			itemsList.Add(items[ordinal]);
		}
		return itemsList;
	}

	public static void Add<T1, T2>(this List<Tuple<T1, T2>> valueTupleList, T1 itemA, T2 itemB)
	{
		valueTupleList.Add(new Tuple<T1, T2>(itemA, itemB));
	}

	public static List<Streak> MutuallyExclusiveStreaks<T>(this IList<T> input, params Func<T, bool>[] cases)
	{
		_Closure_0024__14_002D0<T> capturedLocals = default;
		_Closure_0024__14_002D0<T> capturedLocals2 = new _Closure_0024__14_002D0<T>(capturedLocals);
		capturedLocals2.cases = cases;
		List<Streak> streaks = new List<Streak>();
		if (input.Count == 0)
		{
			return streaks;
		}
		checked
		{
			if (capturedLocals2.cases.Count() == 0)
			{
				streaks.Add(new Streak(0, input.Count - 1, 0));
				return streaks;
			}
			Func<T, int> func = [SpecialName] (T localValue) =>
			{
				int count = capturedLocals2.cases.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (capturedLocals2.cases[i](localValue))
					{
						return i + 1;
					}
				}
				return 0;
			};
			int func2 = func(input[0]);
			int startIdx = 0;
			int count2 = input.Count - 1;
			for (int startIdx2 = 0; startIdx2 <= count2; startIdx2++)
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

	public static List<Streak> MutuallyExclusiveStreaksIndexed<T>(this IList<T> input, params Func<int, bool>[] cases)
	{
		_Closure_0024__15_002D0<T> capturedLocals = default;
		_Closure_0024__15_002D0<T> capturedLocals2 = new _Closure_0024__15_002D0<T>(capturedLocals);
		capturedLocals2.cases = cases;
		List<Streak> streaks = new List<Streak>();
		if (input.Count == 0)
		{
			return streaks;
		}
		checked
		{
			if (capturedLocals2.cases.Count() == 0)
			{
				streaks.Add(new Streak(0, input.Count - 1, 0));
				return streaks;
			}
			Func<int, int> func = [SpecialName] (int ordinal) =>
			{
				int count = capturedLocals2.cases.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (capturedLocals2.cases[i](ordinal))
					{
						return i;
					}
				}
				return 0;
			};
			int func2 = func(0);
			int startIdx = -1;
			int count2 = input.Count - 1;
			for (int startIdx2 = 0; startIdx2 <= count2; startIdx2++)
			{
				int func3 = func(startIdx2);
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

	public static IEnumerable<T> Connect<T>(this IEnumerable<T> input, IEnumerable<T> append)
	{
		List<T> items = new List<T>();
		items.AddRange(input);
		items.AddRange(append);
		return items;
	}

	public static bool CollectionEquals<T>(this IEnumerable<T> items, IEnumerable<T> other)
	{
		if (items == null || other == null)
		{
			throw new NullReferenceException();
		}
		if (items.Count() != other.Count())
		{
			return false;
		}
		checked
		{
			int count = items.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!items.ElementAtOrDefault(i).Equals(other.ElementAtOrDefault(i)))
				{
					return false;
				}
			}
			return true;
		}
	}
}
