using System.Collections.Generic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Structures;

[StandardModule]
public static class IGroupLayerExtensions
{
	public static int FindMatchingGroup<T>(this T lyr, StructureGroupIdentifier g) where T : FeatureLayer
,IGroupLayer	{
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (GetGroupIdentifier(lyr, i).Matches(g))
				{
					return i;
				}
			}
			return -1;
		}
	}

	public static Dictionary<int, List<int>> ComputeStructureGroupMap<T>(this T lyr) where T : FeatureLayer
,IGroupLayer	{
		Dictionary<int, List<int>> structureGroupMap = new Dictionary<int, List<int>>();
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int structureIndex = lyr.TryGetStructureIndex(i);
				if (structureIndex != -1)
				{
					List<int> structureGroupMapList = null;
					if (structureGroupMap.TryGetValue(structureIndex, out structureGroupMapList))
					{
						structureGroupMapList.Add(i);
						continue;
					}
					structureGroupMapList = new List<int>();
					structureGroupMapList.Add(i);
					structureGroupMap[structureIndex] = structureGroupMapList;
				}
			}
			return structureGroupMap;
		}
	}

	public static int AddNewGroup<T>(this T lyr, int parentStructureIndex, string groupName = "") where T : FeatureLayer
,IGroupLayer	{
		int featureCount = lyr.FeatureCount();
		lyr.AddFeature(null);
		lyr.SetActualStructureIndex(featureCount, parentStructureIndex);
		if (string.IsNullOrEmpty(groupName))
		{
			groupName = StructureElementLayerExtensions.GetUniqueNameWithinStructure(lyr, parentStructureIndex);
		}
		lyr.SetFeatureName(featureCount, groupName);
		return featureCount;
	}

	public static bool ChangeStructureIdx<T>(this T lyr, int fid, int newStructureIndex) where T : FeatureLayer
,IGroupLayer	{
		lyr.SetActualStructureIndex(fid, newStructureIndex);
		string featureName = lyr.GetFeatureName(fid);
		bool condition = false;
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (i != fid && Operators.CompareString(lyr.GetFeatureName(i), featureName, TextCompare: false) == 0)
				{
					condition = true;
					break;
				}
			}
			if (condition)
			{
				lyr.SetFeatureName(fid, StructureElementLayerExtensions.GetUniqueNameWithinStructure(lyr, newStructureIndex));
			}
			return condition;
		}
	}

	public static StructureGroupIdentifier GetGroupIdentifier<T>(this T lyr, int fID) where T : FeatureLayer
,IGroupLayer	{
		IStructureLayer associatedStructureLayer = lyr.GetAssociatedStructureLayer();
		int structureIndex = lyr.TryGetStructureIndex(fID);
		if (structureIndex == -1 || associatedStructureLayer == null)
		{
			return new StructureGroupIdentifier(null, lyr.GetFeatureName(fID));
		}
		return new StructureGroupIdentifier(associatedStructureLayer.GetStructureIdentifier(structureIndex), lyr.GetFeatureName(fID));
	}

	public static int GetGroupNameCharacterLimit<T>(this T lyr) where T : FeatureLayer
,IGroupLayer	{
		return 12;
	}
}
