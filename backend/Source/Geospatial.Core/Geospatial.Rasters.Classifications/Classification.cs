using System;
using System.Collections.Generic;
using Utility.Extensions;

namespace Geospatial.Rasters.Classifications;

public class Classification : IClassification, IMutableSpatialVersion, ISpatialVersion
{
	public const int NoDataValue = 0;

	public const string NoDataString = "NoData";

	private readonly Dictionary<int, string> _classes;

	private Freezer _freezer;

	private long _versionXy;

	public IReadOnlyCollection<int> IDs => _classes.Keys;

	public IReadOnlyCollection<string> Classes => _classes.Values;

	public int Count => _classes.Count;

	public long VersionXY => _versionXy;

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public void ForceSetVersion(long versionXy)
	{
		_versionXy = versionXy;
	}

	public Classification()
	{
		_classes = new Dictionary<int, string>();
		EnsureHasNoData();
	}

	public Classification(Dictionary<int, string> classes)
	{
		_classes = new Dictionary<int, string>(classes);
		EnsureHasNoData();
	}

	private void EnsureHasNoData()
	{
		_classes[0] = "NoData";
		_versionXy++;
	}

	public bool ContainsKey(int key)
	{
		return _classes.ContainsKey(key);
	}

	public bool ContainsValue(string stringValue)
	{
		return _classes.ContainsValue(stringValue);
	}

	public void Set(int id, string name)
	{
		_classes[id] = name;
		_versionXy++;
	}

	public static void AddRangeAsAvailableIDs(IClassification cls, IList<string> classNames)
	{
		int classNamesClsTerm = classNames.Count + cls.Count + 1;
		int unity = 1;
		for (int i = 0; i < classNames.Count; i++)
		{
			string className = classNames[i];
			if (cls.GetID(className) != -1)
			{
				continue;
			}
			while (!string.IsNullOrEmpty(cls.GetName(unity)))
			{
				unity++;
				if (unity > classNamesClsTerm)
				{
					throw new Exception("Logic error or bad classification table formation");
				}
			}
			cls.Set(unity, className);
			unity++;
		}
	}

	public static void AddAsNextAvailableID(IClassification cls, string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			throw new Exception("Illegal classification name " + name.InDoubleQuotes());
		}
		int getID = cls.GetID(name);
		if (getID != -1)
		{
			return;
		}
		for (int i = 1; i <= cls.Count; i++)
		{
			if (string.IsNullOrEmpty(cls.GetName(i)))
			{
				getID = i;
				break;
			}
		}
		if (getID == -1)
		{
			throw new Exception("Invalid ID Table");
		}
		cls.Set(getID, name);
	}

	public Dictionary<int, string> GetAllClassifications()
	{
		return _classes;
	}

	public int GetID(string className)
	{
		if (NameMatches(className, "NoData"))
		{
			return 0;
		}
		foreach (KeyValuePair<int, string> @class in _classes)
		{
			if (NameMatches(className, @class.Value))
			{
				return @class.Key;
			}
		}
		return -1;
	}

	public static bool IsValidClassificationName(string clsName)
	{
		if (string.IsNullOrEmpty(clsName))
		{
			return false;
		}
		if (clsName.Contains("/") || clsName.Contains("\\"))
		{
			return false;
		}
		if (NameMatches(clsName, "NoData"))
		{
			return false;
		}
		return true;
	}

	public static bool NameMatches(string a, string b)
	{
		return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	public string GetName(int intValue)
	{
		if (intValue == 0)
		{
			return "NoData";
		}
		if (_classes.TryGetValue(intValue, out var localValue))
		{
			return localValue;
		}
		return string.Empty;
	}

	public Classification Clone()
	{
		return new Classification(_classes);
	}
}
