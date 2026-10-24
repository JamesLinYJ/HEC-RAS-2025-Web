using System.Collections.Generic;
using Utility.DataStructures;

namespace Ras;

public class ClassificationStandard
{
	public string Name { get; init; }

	public string Description { get; init; }

	public IReadOnlyMap<int, string> KeyMap { get; }

	public ClassificationStandard()
	{
		KeyMap = new Map<int, string>();
	}

	public ClassificationStandard(string name)
	{
		Name = name;
		KeyMap = new Map<int, string>();
	}

	public ClassificationStandard(string name, string desc)
	{
		Name = name;
		Description = desc;
		KeyMap = new Map<int, string>();
	}

	public ClassificationStandard(string name, string desc, Dictionary<int, string> baseMap)
	{
		Name = name;
		Description = desc;
		Map<int, string> map = new Map<int, string>();
		foreach (KeyValuePair<int, string> baseMap2 in baseMap)
		{
			map.Add(baseMap2.Key, baseMap2.Value);
		}
		KeyMap = map;
	}
}
