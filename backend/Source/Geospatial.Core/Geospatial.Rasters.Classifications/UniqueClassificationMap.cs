using System;
using System.Collections.Generic;
using Utility.DataStructures;

namespace Geospatial.Rasters.Classifications;

public class UniqueClassificationMap
{
	public const int DefaultNoDataValue = 0;

	public const int DefaultMissingValue = 9999;

	public Map<int, string> Map { get; }

	public IEnumerable<int> Keys => Map.Forward.Keys;

	public IEnumerable<string> Values => Map.Forward.Values;

	public int Count => Map.Count;

	public UniqueClassificationMap()
	{
		Map = new Map<int, string>();
	}

	public UniqueClassificationMap(Map<int, string> map)
	{
		Map = map;
	}

	public void Clear()
	{
		Map.Clear();
	}

	public int GetBestID(string className)
	{
		if (Map.Backward.TryGetValue(className, out var localValue))
		{
			return localValue;
		}
		return 9999;
	}

	public bool TryGetID(string className, out int intValue)
	{
		return Map.Backward.TryGetValue(className, out intValue);
	}

	public bool TryGetName(int id, out string className)
	{
		return Map.Forward.TryGetValue(id, out className);
	}

	public int AddClassifier(string name)
	{
		if (Map.ContainsValue(name))
		{
			throw new Exception("Classifier already exists.");
		}
		int nextAvailableCode = GetNextAvailableCode();
		if (nextAvailableCode == -1)
		{
			throw new Exception("Classifier full!");
		}
		Map.Add(nextAvailableCode, name);
		return nextAvailableCode;
	}

	public void Set(int intValue, string cls)
	{
		Map.Add(intValue, cls);
	}

	public bool TryAddClassifier(string name, out int code)
	{
		if (Map.ContainsValue(name))
		{
			code = -1;
			return false;
		}
		code = GetNextAvailableCode();
		if (code == -1)
		{
			return false;
		}
		Map.Add(code, name);
		return true;
	}

	private int GetNextAvailableCode()
	{
		for (int i = 1; i <= int.MaxValue; i++)
		{
			if (!Map.ContainsKey(i))
			{
				return i;
			}
		}
		return -1;
	}
}
