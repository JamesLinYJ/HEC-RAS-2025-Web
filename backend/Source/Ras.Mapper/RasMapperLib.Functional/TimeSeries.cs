using System;
using System.Collections.Generic;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Functional;

public abstract class TimeSeries
{
	public const string sH5TypeAttribute = "Type";

	public const string sH5VariableAttribute = "Variable";

	public const string sH5UnitsAttribute = "Units";

	public string Variable { get; }

	public string Units { get; }

	public abstract DateTime StartTime { get; }

	public abstract DateTime EndTime { get; }

	public abstract double Sample(DateTime dt, bool canExtrapolate = false);

	protected abstract void SaveInternal(H5Writer hw, string groupName);

	public virtual void Save(H5Writer hw, string groupName)
	{
		if (hw.ExistsAsGroup(groupName))
		{
			hw.Delete(groupName);
		}
		hw.CreateGroup(groupName);
		hw.WriteAttribute(groupName, "Variable", Variable);
		hw.WriteAttribute(groupName, "Units", Units);
		SaveInternal(hw, groupName);
	}

	protected static void LoadVariableUnits(H5Reader hr, string groupName, ref string var, ref string units)
	{
		hr.TryReadAttributeScalar(groupName, "Variable", ref var);
		hr.TryReadAttributeScalar(groupName, "Units", ref units);
	}

	public TimeSeries()
	{
	}

	public TimeSeries(string variable, string units)
	{
		Variable = variable;
		Units = units;
	}

	public static List<TimeSeries> LoadAll(H5Reader h5r, string searchSubFolder)
	{
		List<TimeSeries> timeSeries = new List<TimeSeries>();
		foreach (string groupName in h5r.GetGroupNames(searchSubFolder))
		{
			string searchSubFolderGroupNameTerm = searchSubFolder + "/" + groupName;
			string tryReadStringAttributeScalar = h5r.TryReadStringAttributeScalar(searchSubFolderGroupNameTerm, "Type");
			if (!string.IsNullOrEmpty(tryReadStringAttributeScalar) && Operators.CompareString(tryReadStringAttributeScalar, "Regular Time Series", TextCompare: false) == 0)
			{
				RegularTimeSeries regularTimeSeries = RegularTimeSeries.TryLoad(h5r, searchSubFolderGroupNameTerm);
				if (regularTimeSeries != null)
				{
					timeSeries.Add(regularTimeSeries);
				}
			}
		}
		return timeSeries;
	}
}
