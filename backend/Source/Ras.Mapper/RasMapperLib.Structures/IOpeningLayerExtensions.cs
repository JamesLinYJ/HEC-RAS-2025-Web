using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Structures;

[StandardModule]
public static class IOpeningLayerExtensions
{
	[CompilerGenerated]
private sealed class _Closure_0024__2_002D0<_0024CLS0> where _0024CLS0 : FeatureLayer
,IOpeningLayer	{
		public _0024CLS0 lyr;

		public _Closure_0024__2_002D0(_Closure_0024__2_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				lyr = other.lyr;
			}
		}

		[SpecialName]
		internal string _Lambda_0024__0(int f)
		{
			return lyr.GetFeatureName(f);
		}
	}

	public static float GetWidthFromGroup<T>(this T lyr, int fid) where T : IOpeningLayer
	{
		int groupIndex = lyr.TryGetGroupIndex(fid);
		if (groupIndex == -1)
		{
			return 0f;
		}
		return lyr.GetAssociatedGroupLayer()?.GetWidth(groupIndex) ?? 0f;
	}

	public static string GetPrefix<T>(this T lyr) where T : FeatureLayer
,IOpeningLayer	{
		return lyr.SimpleFeatureTypeName + " #";
	}

	public static string GetUniqueNameWithinGroup<T>(this T lyr, int groupFId) where T : FeatureLayer
,IOpeningLayer	{
		_Closure_0024__2_002D0<T> capturedLocals = default;
		_Closure_0024__2_002D0<T> capturedLocals2 = new _Closure_0024__2_002D0<T>(capturedLocals);
		capturedLocals2.lyr = lyr;
		string prefix = GetPrefix(capturedLocals2.lyr);
		if (groupFId == -1)
		{
			return capturedLocals2.lyr.GetUniqueFeatureName(prefix);
		}
		List<int> elementsWithinGroup = FindElementsWithinGroup(capturedLocals2.lyr, groupFId);
		HashSet<string> toHashSet = elementsWithinGroup.Select([SpecialName] (int f) => capturedLocals2.lyr.GetFeatureName(f)).ToHashSet();
		int count = elementsWithinGroup.Count;
		for (int ordinal = 1; ordinal <= count; ordinal = checked(ordinal + 1))
		{
			string prefixTerm = prefix + ordinal;
			if (!toHashSet.Contains(prefixTerm))
			{
				return prefixTerm;
			}
		}
		return prefix + elementsWithinGroup.Count;
	}

	public static Dictionary<int, List<int>> ComputeGroupOpeningMap<T>(this T lyr) where T : FeatureLayer
,IOpeningLayer	{
		Dictionary<int, List<int>> groupOpeningMap = new Dictionary<int, List<int>>();
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int groupIndex = lyr.TryGetGroupIndex(i);
				if (groupIndex != -1)
				{
					List<int> groupOpeningMapList = null;
					if (groupOpeningMap.TryGetValue(groupIndex, out groupOpeningMapList))
					{
						groupOpeningMapList.Add(i);
						continue;
					}
					groupOpeningMapList = new List<int>();
					groupOpeningMapList.Add(i);
					groupOpeningMap[groupIndex] = groupOpeningMapList;
				}
			}
			return groupOpeningMap;
		}
	}

	public static List<int> FindElementsWithinGroup<T>(this T lyr, int grpIdx) where T : FeatureLayer
,IOpeningLayer	{
		List<int> elementsWithinGroup = new List<int>();
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (lyr.TryGetGroupIndex(i) == grpIdx)
				{
					elementsWithinGroup.Add(i);
				}
			}
			return elementsWithinGroup;
		}
	}

	public static void WriteCellsConnectivity<T>(this T lyr, H5Writer hw, string datasetname, string structIdColName, string groupColName, string openingColName, Func<int, List<MeshFV2D.ProfileCellRange>> getCellList) where T : FeatureLayer
,IOpeningLayer	{
		int getCellListCountSum = 0;
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				List<MeshFV2D.ProfileCellRange> getCellListList = getCellList(i);
				if (getCellListList != null)
				{
					getCellListCountSum += getCellListList.Count;
				}
			}
			if (getCellListCountSum == 0)
			{
				if (hw.ExistsAsDataset(datasetname))
				{
					hw.Delete(datasetname);
				}
				return;
			}
			int[] intValues = new int[getCellListCountSum - 1 + 1];
			int[] intValuesArray = new int[getCellListCountSum - 1 + 1];
			int[] intValues2 = new int[getCellListCountSum - 1 + 1];
			int[] intValues3 = new int[getCellListCountSum - 1 + 1];
			float[] floatValues = new float[getCellListCountSum - 1 + 1];
			float[] floatValuesArray = new float[getCellListCountSum - 1 + 1];
			int ordinal = 0;
			int count2 = lyr.FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				int structureIndex = lyr.TryGetStructureIndex(j);
				int groupIndex = lyr.TryGetGroupIndex(j);
				List<MeshFV2D.ProfileCellRange> getCellList2 = getCellList(j);
				if (getCellList2 == null)
				{
					continue;
				}
				foreach (MeshFV2D.ProfileCellRange getCellList3 in getCellList2)
				{
					intValues[ordinal] = structureIndex;
					intValuesArray[ordinal] = groupIndex;
					intValues2[ordinal] = j;
					intValues3[ordinal] = getCellList3.Cell;
					floatValues[ordinal] = (float)getCellList3.StationStart;
					floatValuesArray[ordinal] = (float)getCellList3.StationEnd;
					ordinal++;
				}
			}
			CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
			compoundTypeDefinition.AddField<int>(structIdColName);
			compoundTypeDefinition.AddField<int>(groupColName);
			compoundTypeDefinition.AddField<int>(openingColName);
			compoundTypeDefinition.AddField<int>("Cell Index");
			compoundTypeDefinition.AddField<float>("Station Start");
			compoundTypeDefinition.AddField<float>("Station End");
			hw.Create1DCompoundDataset(datasetname, getCellListCountSum, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, datasetname, getCellListCountSum);
			compoundWriter.WriteColumn(0, intValues);
			compoundWriter.WriteColumn(1, intValuesArray);
			compoundWriter.WriteColumn(2, intValues2);
			compoundWriter.WriteColumn(3, intValues3);
			compoundWriter.WriteColumn(4, floatValues);
			compoundWriter.WriteColumn(5, floatValuesArray);
		}
	}

	public static int GetOpeningNameCharacterLimit<T>(this T lyr) where T : FeatureLayer
,IOpeningLayer	{
		return 32;
	}
}
