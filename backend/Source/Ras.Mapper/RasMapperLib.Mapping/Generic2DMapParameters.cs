using System.Collections.Generic;
using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib.Mapping;

public class Generic2DMapParameters
{
	public const string sGroup = "Group";

	public const string sName = "Name";

	public const string sUnits = "Units";

	public const string sLocation = "Location";

	public const string sCoverage = "Coverage";

	public const string sOrientation = "Orientation";

	public const string sCanPlot = "Can Plot";

	public const string sIndex = "Index";

	public const string sMax = "Maximum Value of Data Set";

	public const string sMin = "Minimum Value of Data Set";

	public const string sCanInterpolate = "Can Interpolate";

	public string Group;

	public bool HasGroup;

	public string Name;

	public string Units;

	public Location Location;

	public Coverage Coverage;

	public Orientation Orientation;

	public bool CanPlot;

	public int Index;

	public bool HasMinMax;

	public float Max;

	public float Min;

	public bool CanInterpolate;

	public string HDFDataset;

	public Generic2DMapParameters()
	{
	}

	public Generic2DMapParameters(Generic2DMapParameters copy)
	{
		Group = copy.Group;
		HasGroup = copy.HasGroup;
		Name = copy.Name;
		Units = copy.Units;
		Location = copy.Location;
		Coverage = copy.Coverage;
		Orientation = copy.Orientation;
		CanPlot = copy.CanPlot;
		Index = copy.Index;
		HasMinMax = copy.HasMinMax;
		Max = copy.Max;
		Min = copy.Min;
		CanInterpolate = copy.CanInterpolate;
		HDFDataset = copy.HDFDataset;
	}

	public static Dictionary<string, List<Generic2DMapParameters>> AvailableGenericDatasets(H5Reader hr, string d2FlowAreaName)
	{
		return AvailableGenericDatasets(hr, "Base Output", d2FlowAreaName);
	}

	public static bool HasGenericDatasets(H5Reader hr, string outputBlockName, string d2FlowAreaName)
	{
		string outputBlockNameD2FlowAreaNameTerm = "Results/Unsteady/Output/Output Blocks/" + outputBlockName + "/Unsteady Time Series/2D Flow Areas/" + d2FlowAreaName;
		if (!hr.ExistsAsGroup(outputBlockNameD2FlowAreaNameTerm))
		{
			return false;
		}
		List<string> datasetNames = hr.GetDatasetNames(outputBlockNameD2FlowAreaNameTerm);
		foreach (string datasetName in datasetNames)
		{
			string fullDset = outputBlockNameD2FlowAreaNameTerm + "/" + datasetName;
			if (IsValid(hr, fullDset))
			{
				return true;
			}
		}
		return false;
	}

	public static Dictionary<string, List<Generic2DMapParameters>> AvailableGenericDatasets(H5Reader hr, string outputBlockName, string d2FlowAreaName)
	{
		string outputBlockNameD2FlowAreaNameTerm = "Results/Unsteady/Output/Output Blocks/" + outputBlockName + "/Unsteady Time Series/2D Flow Areas/" + d2FlowAreaName;
		Dictionary<string, List<Generic2DMapParameters>> generic2DMapParametersListByString = new Dictionary<string, List<Generic2DMapParameters>>();
		List<string> datasetNames = hr.GetDatasetNames(outputBlockNameD2FlowAreaNameTerm);
		foreach (string datasetName in datasetNames)
		{
			string datasetNameTerm = outputBlockNameD2FlowAreaNameTerm + "/" + datasetName;
			if (IsValid(hr, datasetNameTerm))
			{
				Generic2DMapParameters mapParams = null;
				Parse(hr, datasetNameTerm, ref mapParams);
				if (!generic2DMapParametersListByString.ContainsKey(mapParams.Group))
				{
					generic2DMapParametersListByString[mapParams.Group] = new List<Generic2DMapParameters>();
				}
				generic2DMapParametersListByString[mapParams.Group].Add(mapParams);
			}
		}
		return generic2DMapParametersListByString;
	}

	public static Generic2DMapParameters Generic2DParametersFromDataSet(H5Reader hr, string outputBlockname, string d2FlowAreaName, string dataset)
	{
		string outputBlocknameD2FlowAreaNameTerm = "Results/Unsteady/Output/Output Blocks/" + outputBlockname + "/Unsteady Time Series/2D Flow Areas/" + d2FlowAreaName;
		new Dictionary<string, List<Generic2DMapParameters>>();
		List<string> datasetNames = hr.GetDatasetNames(outputBlocknameD2FlowAreaNameTerm);
		foreach (string datasetName in datasetNames)
		{
			string datasetNameTerm = outputBlocknameD2FlowAreaNameTerm + "/" + datasetName;
			if (Operators.CompareString(datasetName, dataset, TextCompare: false) == 0 && IsValid(hr, datasetNameTerm))
			{
				Generic2DMapParameters mapParams = null;
				Parse(hr, datasetNameTerm, ref mapParams);
				return mapParams;
			}
		}
		return null;
	}

	private static bool IsValid(H5Reader hr, string fullDset)
	{
		if (hr.ExistsAsAttribute(fullDset, "Location") && hr.ExistsAsAttribute(fullDset, "Coverage"))
		{
			return hr.ExistsAsAttribute(fullDset, "Orientation");
		}
		return false;
	}

	public static bool Parse(H5Reader hr, string datasetFullPath, ref Generic2DMapParameters mapParams)
	{
		if (!hr.ExistsAsDataset(datasetFullPath))
		{
			return false;
		}
		mapParams = new Generic2DMapParameters();
		string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(datasetFullPath, "Location");
		string tryReadStringAttributeScalar2 = hr.TryReadStringAttributeScalar(datasetFullPath, "Coverage");
		string tryReadStringAttributeScalar3 = hr.TryReadStringAttributeScalar(datasetFullPath, "Orientation");
		mapParams.Group = hr.TryReadStringAttributeScalar(datasetFullPath, "Group").Trim();
		mapParams.Name = hr.TryReadStringAttributeScalar(datasetFullPath, "Name").Trim();
		mapParams.Units = hr.TryReadStringAttributeScalar(datasetFullPath, "Units").Trim();
		int index = 0;
		if (hr.TryReadAttributeScalar(datasetFullPath, "Index", ref index))
		{
			mapParams.Index = index;
		}
		if (hr.ExistsAsAttribute(datasetFullPath, "Maximum Value of Data Set") && hr.ExistsAsAttribute(datasetFullPath, "Minimum Value of Data Set"))
		{
			float max = 0f;
			if (hr.TryReadAttributeScalar(datasetFullPath, "Maximum Value of Data Set", ref max))
			{
				mapParams.Max = max;
			}
			float min = 0f;
			if (hr.TryReadAttributeScalar(datasetFullPath, "Minimum Value of Data Set", ref min))
			{
				mapParams.Min = min;
			}
			mapParams.HasMinMax = true;
		}
		else
		{
			mapParams.HasMinMax = false;
		}
		if (hr.ExistsAsAttribute(datasetFullPath, "Can Interpolate"))
		{
			string tryReadStringAttributeScalar4 = hr.TryReadStringAttributeScalar(datasetFullPath, "Can Interpolate");
			bool result = true;
			if (bool.TryParse(tryReadStringAttributeScalar4, out result))
			{
				mapParams.CanInterpolate = result;
			}
			else
			{
				mapParams.CanInterpolate = true;
			}
		}
		else
		{
			mapParams.CanInterpolate = true;
		}
		mapParams.HDFDataset = datasetFullPath.Split('/').Last();
		if (string.IsNullOrWhiteSpace(mapParams.Group))
		{
			mapParams.Group = mapParams.HDFDataset;
		}
		else
		{
			mapParams.HasGroup = true;
		}
		mapParams.Location = ParseLocation(tryReadStringAttributeScalar);
		if (mapParams.Location == Location.Unknown)
		{
			return false;
		}
		mapParams.Coverage = ParseCoverage(tryReadStringAttributeScalar2);
		if (mapParams.Coverage == Coverage.Unknown)
		{
			return false;
		}
		mapParams.Orientation = ParseOrientation(tryReadStringAttributeScalar3);
		if (mapParams.Orientation == Orientation.Unknown)
		{
			return false;
		}
		if (hr.ExistsAsAttribute(datasetFullPath, "Can Plot"))
		{
			string tryReadStringAttributeScalar5 = hr.TryReadStringAttributeScalar(datasetFullPath, "Can Plot");
			bool result2 = true;
			if (bool.TryParse(tryReadStringAttributeScalar5, out result2))
			{
				mapParams.CanPlot = result2;
			}
			else
			{
				mapParams.CanPlot = true;
			}
		}
		else
		{
			mapParams.CanPlot = true;
		}
		return true;
	}

	public static bool Parse(H5Reader hr, string d2FlowAreaName, string dsetName, ref Generic2DMapParameters mapParams)
	{
		string datasetFullPath = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas//" + d2FlowAreaName + "/" + dsetName;
		return Parse(hr, datasetFullPath, ref mapParams);
	}

	public static Location ParseLocation(string stringValue)
	{
		if (stringValue.RoughlyEquals("cell") || stringValue.RoughlyEquals("cells"))
		{
			return Location.Cell;
		}
		if (stringValue.RoughlyEquals("face") || stringValue.RoughlyEquals("faces"))
		{
			return Location.Face;
		}
		if (stringValue.RoughlyEquals("subcell") || stringValue.RoughlyEquals("subcells"))
		{
			return Location.SubCell;
		}
		if (stringValue.RoughlyEquals("subface") || stringValue.RoughlyEquals("subfaces"))
		{
			return Location.SubFace;
		}
		return Location.Unknown;
	}

	public static Coverage ParseCoverage(string stringValue)
	{
		if (stringValue.RoughlyEquals("average"))
		{
			return Coverage.Average;
		}
		if (stringValue.RoughlyEquals("wet"))
		{
			return Coverage.Wet;
		}
		if (stringValue.RoughlyEquals("dry"))
		{
			return Coverage.Dry;
		}
		return Coverage.Unknown;
	}

	public static Orientation ParseOrientation(string stringValue)
	{
		if (stringValue.RoughlyEquals("scalar"))
		{
			return Orientation.Scalar;
		}
		if (stringValue.RoughlyEquals("normal"))
		{
			return Orientation.Normal;
		}
		return Orientation.Unknown;
	}
}
