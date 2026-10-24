using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public struct TileCacheComputeOptions
{
	public string DatasetIdentifier;

	public int MaxZoom;

	public string Filename;

	public string ProjectName;

	public string PlanName;

	public string MapTypeName;

	public string LegendValuesCSV;

	public string LegendColorsCSV;

	public List<Tuple<string, int>> DesiredProfiles;

	public TileCacheComputeOptions(RASResultsMap map, string outputFilename, int maxZoom, string tableIdentifier, List<Tuple<string, int>> desiredProfiles)
	{
		this = default;
		ConstructInternal(map, outputFilename, maxZoom, tableIdentifier, desiredProfiles);
	}

	public TileCacheComputeOptions(RASResultsMap map, string outputFilename, int maxZoom, string tableIdentifier, List<string> desiredProfiles)
	{
		this = default;
		RASResults results = map.Results;
		List<Tuple<string, int>> valueTupleList = new List<Tuple<string, int>>();
		foreach (string desiredProfile in desiredProfiles)
		{
			switch (desiredProfile)
			{
			case "None":
				valueTupleList.Add(new Tuple<string, int>(desiredProfile, -1));
				break;
			case "Max":
				valueTupleList.Add(new Tuple<string, int>(desiredProfile, int.MaxValue));
				break;
			case "Min":
				valueTupleList.Add(new Tuple<string, int>(desiredProfile, int.MinValue));
				break;
			default:
				valueTupleList.Add(new Tuple<string, int>(desiredProfile, ArrayExtensionMethods.IndexOf(results.ProfileNames, desiredProfile)));
				break;
			}
		}
		ConstructInternal(map, outputFilename, maxZoom, tableIdentifier, valueTupleList);
	}

	public TileCacheComputeOptions(RASResultsMap map, string outputFilename, int maxZoom, string tableIdentifier, List<int> desiredProfiles)
	{
		this = default;
		RASResults results = map.Results;
		List<Tuple<string, int>> valueTupleList = new List<Tuple<string, int>>();
		foreach (int desiredProfile in desiredProfiles)
		{
			switch (desiredProfile)
			{
			case -1:
				valueTupleList.Add(new Tuple<string, int>("None", desiredProfile));
				break;
			case int.MaxValue:
				valueTupleList.Add(new Tuple<string, int>("Max", desiredProfile));
				break;
			case int.MinValue:
				valueTupleList.Add(new Tuple<string, int>("Min", desiredProfile));
				break;
			default:
				valueTupleList.Add(new Tuple<string, int>(results.ProfileNames[desiredProfile], desiredProfile));
				break;
			}
		}
		ConstructInternal(map, outputFilename, maxZoom, tableIdentifier, valueTupleList);
	}

	private void ConstructInternal(RASResultsMap map, string outputFilename, int maxZoom, string tableIdentifier, List<Tuple<string, int>> desiredProfiles)
	{
		ProjectName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(map.Results.SourceFilename));
		PlanName = map.Results.PlanAttributes.PlanShortID;
		MapTypeName = map.MapType.DefaultLayerName;
		Filename = outputFilename;
		DatasetIdentifier = tableIdentifier;
		string directoryName = Path.GetDirectoryName(outputFilename);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		MaxZoom = maxZoom;
		DesiredProfiles = desiredProfiles;
	}
}
