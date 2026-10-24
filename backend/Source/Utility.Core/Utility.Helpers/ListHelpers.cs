using System;
using System.Collections.Generic;

namespace Utility.Helpers;

public static class ListHelpers
{
	public static void LinearInterpolateNaNsInPlace(int count, Func<int, double> getValue, Action<int, double> setValue)
	{
		LinearInterpolateNaNsInPlace((getValue, setValue), count, ((Func<int, double>, Action<int, double>) model, int idx) => model.Item1(idx), ((Func<int, double>, Action<int, double>) model, int idx, double computed) =>
		{
			model.Item2(idx, computed);
		});
	}

	public static void LinearInterpolateNaNsInPlace<T>(T model, int count, Func<T, int, double> getValue, Action<T, int, double> setValue)
	{
		List<int> intList = new List<int>();
		for (int i = 0; i < count; i++)
		{
			if (!double.IsNaN(getValue(model, i)))
			{
				intList.Add(i);
			}
		}
		for (int j = 0; j < intList.Count - 1; j++)
		{
			int ordinal = intList[j];
			int ordinal2 = intList[j + 1];
			if (ordinal2 != ordinal + 1)
			{
				double getValue2 = getValue(model, ordinal);
				double getValue3 = getValue(model, ordinal2);
				double computed = ordinal2 - ordinal;
				double getValue3GetValue2Term = (getValue3 - getValue2) / computed;
				for (int k = 0; (double)k < computed - 1.0; k++)
				{
					int ordinal3 = ordinal + k + 1;
					setValue(model, ordinal3, getValue2 + getValue3GetValue2Term * (double)(k + 1));
				}
			}
		}
	}

	public static void Reverse<T>(IList<T> items)
	{
		int ordinal = 0;
		int countOffset = items.Count - 1;
		while (ordinal < countOffset)
		{
			int index = ordinal;
			int index2 = countOffset;
			T localValue = items[countOffset];
			T localValue2 = items[ordinal];
			items[index] = localValue;
			items[index2] = localValue2;
			ordinal++;
			countOffset--;
		}
	}
}
