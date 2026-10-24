using System;
using System.Collections.Generic;
using System.Diagnostics;
using Utility.Extensions;

namespace Geospatial.Rasters.Classifications;

[DebuggerDisplay("{VariableName}")]
public class Parameter
{
	public string VariableName { get; }

	public Dictionary<string, float> Values { get; }

	public Parameter(string varName)
	{
		VariableName = varName;
		Values = MakeDictionary();
	}

	public Parameter(string varName, Dictionary<string, float> values)
	{
		VariableName = varName;
		Values = MakeDictionary();
		foreach (KeyValuePair<string, float> localValue in values)
		{
			Values[localValue.Key] = localValue.Value;
		}
	}

	public void AddOverride(string cls, float floatValue)
	{
		Values[cls] = floatValue;
	}

	public void RemoveOverride(string cls)
	{
		Values.Remove(cls);
	}

	public void OverrideNoData(float floatValue)
	{
		AddOverride("NoData", floatValue);
	}

	public void StripNoData()
	{
		Values.RemoveWhere((KeyValuePair<string, float> kvp) => kvp.Value == float.MaxValue || kvp.Value == -9999f || float.IsNaN(kvp.Value));
	}

	private static Dictionary<string, float> MakeDictionary()
	{
		return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
	}

	public Parameter MergeWith(Dictionary<string, float> otherValues)
	{
		Dictionary<string, float> makeDictionary = MakeDictionary();
		foreach (KeyValuePair<string, float> localValue in Values)
		{
			makeDictionary[localValue.Key] = localValue.Value;
		}
		foreach (KeyValuePair<string, float> otherValue in otherValues)
		{
			makeDictionary[otherValue.Key] = otherValue.Value;
		}
		return new Parameter(VariableName, makeDictionary);
	}

	public Parameter MergeWith(Parameter other)
	{
		return MergeWith(other.Values);
	}

	public Parameter OverrideWith(Dictionary<string, float> otherValues)
	{
		Dictionary<string, float> makeDictionary = MakeDictionary();
		foreach (KeyValuePair<string, float> localValue in Values)
		{
			if (otherValues.TryGetValue(localValue.Key, out var makeDictionary2))
			{
				makeDictionary[localValue.Key] = makeDictionary2;
			}
			else
			{
				makeDictionary[localValue.Key] = localValue.Value;
			}
		}
		return new Parameter(VariableName, makeDictionary);
	}

	public Parameter OverrideWith(Parameter other)
	{
		return OverrideWith(other.Values);
	}

	public Dictionary<int, float> GetIntegerLookup(IClassification classifier)
	{
		Dictionary<int, float> integerLookup = new Dictionary<int, float>();
		foreach (KeyValuePair<int, string> allClassification in classifier.GetAllClassifications())
		{
			int key = allClassification.Key;
			string allClassificationTerm = allClassification.Value;
			if (Values.TryGetValue(allClassificationTerm, out var integerLookup2))
			{
				integerLookup[key] = integerLookup2;
			}
		}
		return integerLookup;
	}

	public Parameter Clone()
	{
		return new Parameter(VariableName, Values);
	}

	private static bool IsNoData(float computed)
	{
		return computed == -9999f;
	}
}
