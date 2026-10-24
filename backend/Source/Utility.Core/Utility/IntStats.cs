using System.Collections.Generic;
using System.Linq;
using Utility.DataStructures;

namespace Utility;

public class IntStats
{
	public int TotalCount;

	public SortedDictionary<int, int> InstanceCount;

	public TranslatedList<int, float> InstanceFrequency;

	public static IntStats Compute(IList<int> data)
	{
		IntStats intStats = new IntStats();
		intStats.AddData(data);
		return intStats;
	}

	public IntStats()
	{
		InstanceCount = new SortedDictionary<int, int>();
		InstanceFrequency = new TranslatedList<int, float>(() => InstanceCount.Keys.ToList(), (int k) => (float)InstanceCount[k] / (float)TotalCount);
	}

	public void AddData(IList<int> data)
	{
		TotalCount += data.Count;
		for (int i = 0; i < data.Count; i++)
		{
			int key = data[i];
			if (InstanceCount.TryGetValue(key, out var localValue))
			{
				InstanceCount[key] = localValue + 1;
			}
			else
			{
				InstanceCount[key] = 1;
			}
		}
	}
}
