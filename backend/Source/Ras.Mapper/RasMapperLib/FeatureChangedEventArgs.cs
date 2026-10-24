using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class FeatureChangedEventArgs : EventArgs
{
	public readonly FeatureChangedType ChangeType;

	public readonly IEnumerable<int> FeatureIndicesChanged;

	public readonly IEnumerable<Feature> FeaturesDeleted;

	public readonly FeatureChangedEventArgs SubFeatureChanged;

	public readonly Transform ChangedTransform;

	public FeatureChangedEventArgs(int idx, FeatureChangedType changeType, IEnumerable<int> subIdxs, FeatureChangedType subChangeType, Transform transform = null)
		: this(idx, changeType, transform)
	{
		SubFeatureChanged = new FeatureChangedEventArgs(subIdxs, subChangeType, transform);
	}

	public FeatureChangedEventArgs(int idx, FeatureChangedType changeType, IEnumerable<int> subIdxs, FeatureChangedType subChangeType, IEnumerable<Feature> subFeaturesDeleted)
		: this(idx, changeType)
	{
		SubFeatureChanged = new FeatureChangedEventArgs(subIdxs, subChangeType, subFeaturesDeleted);
	}

	public FeatureChangedEventArgs(IEnumerable<int> featuresChanged, FeatureChangedType changeType)
	{
		FeatureIndicesChanged = featuresChanged;
		ChangeType = changeType;
	}

	public FeatureChangedEventArgs(IEnumerable<int> featuresChanged, FeatureChangedType changeType, Transform transform)
	{
		FeatureIndicesChanged = featuresChanged;
		ChangeType = changeType;
		ChangedTransform = transform;
	}

	public FeatureChangedEventArgs(IEnumerable<int> featuresChanged, FeatureChangedType changeType, IEnumerable<Feature> featuresDeleted)
	{
		FeatureIndicesChanged = featuresChanged;
		ChangeType = changeType;
		FeaturesDeleted = featuresDeleted;
	}

	public FeatureChangedEventArgs(int idx, FeatureChangedType changeType, Transform transform = null)
	{
		FeatureIndicesChanged = new int[1] { idx };
		ChangeType = changeType;
		ChangedTransform = transform;
	}

	public FeatureChangedEventArgs(int idx, FeatureChangedType changeType, Feature ftDeleted)
	{
		FeatureIndicesChanged = new int[1] { idx };
		ChangeType = changeType;
		FeaturesDeleted = new Feature[1] { ftDeleted };
	}

	public FeatureChangedEventArgs(HashSet<int> idxs, FeatureChangedType changeType)
	{
		FeatureIndicesChanged = idxs.ToList();
		ChangeType = changeType;
	}

	public List<Tuple<int, int>> GetOldNewIndexMap(int currentRowCount)
	{
		List<Tuple<int, int>> oldNewIndexMap = new List<Tuple<int, int>>();
		if (FeatureIndicesChanged.Count() == 0)
		{
			return oldNewIndexMap;
		}
		checked
		{
			if (ChangeType == FeatureChangedType.FeatureAdded)
			{
				List<int> orderBy = FeatureIndicesChanged.OrderBy([SpecialName] (int a) => a).ToList();
				foreach (int orderBy2 in orderBy)
				{
					oldNewIndexMap.Add(new Tuple<int, int>(-1, orderBy2));
				}
				int count = orderBy.Count;
				int currentRowCountAboveCount = currentRowCount - count;
				int ordinal = 0;
				int orderBy3 = orderBy[0];
				int count3 = currentRowCountAboveCount - 1;
				for (int ordinal2 = 0; ordinal2 <= count3; ordinal2++)
				{
					while (ordinal2 >= orderBy3 - ordinal)
					{
						ordinal++;
						orderBy3 = ((ordinal != orderBy.Count) ? orderBy[ordinal] : int.MaxValue);
					}
					if (ordinal != 0)
					{
						oldNewIndexMap.Add(new Tuple<int, int>(ordinal2, ordinal2 + ordinal));
					}
				}
			}
			else if (ChangeType == FeatureChangedType.FeatureDeleted)
			{
				List<int> orderByList = FeatureIndicesChanged.OrderBy([SpecialName] (int a) => a).ToList();
				foreach (int orderBy4 in orderByList)
				{
					oldNewIndexMap.Add(new Tuple<int, int>(orderBy4, -1));
				}
				int count2 = orderByList.Count;
				int currentRowCountPlusCount2 = currentRowCount + count2;
				int ordinal3 = 0;
				int orderBy5 = orderByList[0];
				int count4 = currentRowCountPlusCount2 - 1;
				for (int ordinal4 = 0; ordinal4 <= count4; ordinal4++)
				{
					while (ordinal4 >= orderBy5 - ordinal3)
					{
						ordinal3++;
						orderBy5 = ((ordinal3 != orderByList.Count) ? orderByList[ordinal3] : int.MaxValue);
					}
					if (ordinal3 != 0)
					{
						oldNewIndexMap.Add(new Tuple<int, int>(ordinal4 + ordinal3, ordinal4));
					}
				}
			}
			else
			{
				_ = ChangeType;
			}
			return oldNewIndexMap;
		}
	}
}
