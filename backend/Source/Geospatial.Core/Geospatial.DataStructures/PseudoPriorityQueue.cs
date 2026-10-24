using System;
using System.Collections.Generic;

namespace Geospatial.DataStructures;

public class PseudoPriorityQueue
{
	public int LastValidIndex = -1;

	public int[] Indexes;

	public double[] Values;

	public PseudoPriorityQueue(IList<double> data = null, int sz = 100)
	{
		if (sz < 16)
		{
			sz = 16;
		}
		Indexes = new int[sz];
		Values = new double[sz];
		Regenerate(data);
	}

	public void Regenerate(IList<double> data = null)
	{
		LastValidIndex = -1;
		for (int i = 0; i < Indexes.Length; i++)
		{
			Indexes[i] = -1;
			Values[i] = double.MaxValue;
		}
		if (data != null)
		{
			for (int j = 0; j < data.Count; j++)
			{
				TryAdd(j, data[j]);
			}
		}
	}

	public void TryAdd(int idx, double doubleValue)
	{
		if (LastValidIndex == -1)
		{
			Indexes[0] = idx;
			Values[0] = doubleValue;
			LastValidIndex = 0;
			return;
		}
		if (doubleValue > Values[LastValidIndex])
		{
			if (LastValidIndex != Values.Length - 1)
			{
				LastValidIndex++;
				Indexes[LastValidIndex] = idx;
				Values[LastValidIndex] = doubleValue;
			}
			return;
		}
		Action<int, int> action = (int start, int last) =>
		{
			for (int i = start; i <= last; i++)
			{
				double computed = Values[i];
				if (doubleValue < computed)
				{
					InsertAndPushBack(i, idx, doubleValue);
					break;
				}
			}
		};
		if (LastValidIndex < 8)
		{
			action(0, LastValidIndex);
			return;
		}
		int scaledLastValidIndex = LastValidIndex / 2;
		int ordinal = scaledLastValidIndex / 2;
		while (true)
		{
			if (scaledLastValidIndex <= 0 || ordinal <= 4)
			{
				if (scaledLastValidIndex < 0)
				{
					scaledLastValidIndex = 0;
				}
				int ordinal2 = ordinal * 2 + 1;
				int ordinal3 = scaledLastValidIndex - ordinal2;
				if (ordinal3 < 0)
				{
					ordinal3 = 0;
				}
				int lastValidIndexTerm = scaledLastValidIndex + ordinal2;
				if (lastValidIndexTerm > LastValidIndex)
				{
					lastValidIndexTerm = LastValidIndex;
				}
				action(ordinal3, lastValidIndexTerm);
				return;
			}
			double computed2 = Values[scaledLastValidIndex];
			if (doubleValue == computed2)
			{
				InsertAndPushBack(scaledLastValidIndex, idx, doubleValue);
				return;
			}
			if (doubleValue < computed2)
			{
				double computed3 = Values[scaledLastValidIndex - 1];
				if (doubleValue >= computed3)
				{
					break;
				}
				scaledLastValidIndex -= ordinal;
				ordinal /= 2;
			}
			else
			{
				scaledLastValidIndex += ordinal;
				ordinal /= 2;
			}
		}
		InsertAndPushBack(scaledLastValidIndex, idx, doubleValue);
	}

	public int TakeMin()
	{
		if (LastValidIndex == -1)
		{
			return -1;
		}
		int index = Indexes[0];
		for (int i = 1; i <= LastValidIndex; i++)
		{
			int index2 = Indexes[i];
			if (index2 > index)
			{
				index2--;
			}
			Indexes[i - 1] = index2;
			Values[i - 1] = Values[i];
		}
		LastValidIndex--;
		return index;
	}

	public void NotifyValueChanged(int idx, double newValue)
	{
		bool condition = false;
		for (int i = 0; i <= LastValidIndex; i++)
		{
			if (Indexes[i] == idx)
			{
				RemoveAndPull(i);
				condition = true;
				break;
			}
		}
		if (condition && LastValidIndex > 0)
		{
			double lastValidIndexTerm = Values[LastValidIndex];
			if (newValue < lastValidIndexTerm)
			{
				TryAdd(idx, newValue);
			}
		}
	}

	private void RemoveAndPull(int localIdx)
	{
		for (int i = localIdx + 1; i <= LastValidIndex; i++)
		{
			Indexes[i - 1] = Indexes[i];
			Values[i - 1] = Values[i];
		}
		LastValidIndex--;
	}

	private void InsertAndPushBack(int localIdx, int pushIdx, double computed)
	{
		int pushIdx2 = pushIdx;
		double computed2 = computed;
		for (int i = localIdx; i <= LastValidIndex + 1; i++)
		{
			if (i >= Indexes.Length)
			{
				return;
			}
			int index = Indexes[i];
			double computed3 = Values[i];
			Indexes[i] = pushIdx2;
			Values[i] = computed2;
			pushIdx2 = index;
			computed2 = computed3;
		}
		LastValidIndex++;
	}
}
