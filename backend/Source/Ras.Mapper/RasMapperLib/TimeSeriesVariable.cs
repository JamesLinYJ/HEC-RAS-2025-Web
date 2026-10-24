using System.Collections.Generic;
using System.Diagnostics;
using H5Assist.VB;

namespace RasMapperLib;

[DebuggerDisplay("({VariableName} - {Units})")]
public class TimeSeriesVariable
{
	public string VariableName;

	public string Units;

	public float[] TimeSeries;

	public TimeSeriesVariable(string name, string units, float[] ts)
	{
		VariableName = name;
		Units = units;
		TimeSeries = ts;
	}

	public static List<TimeSeriesVariable> Parse(string h5Filename, string dset)
	{
		using H5Reader h5r = new H5Reader(h5Filename);
		return Parse(h5r, dset);
	}

	public static List<TimeSeriesVariable> Parse(H5Reader h5r, string dset)
	{
		if (!h5r.ExistsAsDataset(dset))
		{
			return null;
		}
		if (!h5r.ExistsAsAttribute(dset, "Variable_Unit"))
		{
			return null;
		}
		float[,] values = null;
		h5r.ReadDataset(dset, ref values);
		string[,] values2 = null;
		h5r.ReadAttribute(dset, "Variable_Unit", ref values2);
		if (values2 == null)
		{
			return null;
		}
		int length = values.GetLength(1);
		if (values2.GetLength(1) != 2)
		{
			return null;
		}
		if (values2.GetLength(0) != length)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
		checked
		{
			int count = length - 1;
			for (int i = 0; i <= count; i++)
			{
				float[] floatValues = new float[values.GetLength(0) - 1 + 1];
				int count2 = floatValues.Length - 1;
				for (int j = 0; j <= count2; j++)
				{
					floatValues[j] = values[j, i];
				}
				timeSeriesVariables.Add(new TimeSeriesVariable(values2[i, 0].Trim(), values2[i, 1].Trim(), floatValues));
			}
			return timeSeriesVariables;
		}
	}
}
