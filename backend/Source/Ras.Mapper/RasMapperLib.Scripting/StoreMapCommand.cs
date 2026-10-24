using System;
using System.Collections.Generic;
using System.Xml.Linq;
using RasMapperLib.Mapping;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class StoreMapCommand : Command
{
	public override string CommandName { get; }

	public MapTypes MapType { get; set; }

	public string Result { get; set; }

	public string OutputBaseFilename { get; set; }

	public string Terrain { get; set; }

	public string ProfileName { get; set; }

	public string ArrivalStartProfile { get; set; }

	public string ArrivalEndProfile { get; set; }

	public TimeUnits TimeUnits { get; set; }

	public bool ComputeSuccessful { get; set; }

	public string ActualOutputFilename { get; set; }

	public StoreMapCommand()
	{
		CommandName = "StoreMap";
		TimeUnits = TimeUnits.None;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		RASResults rASResults = RASResults.TryIdentifyResultsFile(Result);
		if (rASResults == null)
		{
			throw new Exception("Result could not be identified " + Result.InSingleQuotes());
		}
		TerrainLayer terrainLayer;
		if (string.IsNullOrEmpty(Terrain))
		{
			terrainLayer = rASResults.Geometry.Terrain;
		}
		else
		{
			terrainLayer = new TerrainLayer("Terrain", Terrain);
			rASResults.Geometry.SetTerrainTemporary(terrainLayer);
		}
		if (!terrainLayer.AllSourceFilesExist())
		{
			throw new Exception("Invalid terrain - required source files do not exist.");
		}
		RASResultsMap rASResultsMap = new RASResultsMap(rASResults, MapType);
		rASResultsMap.OutputMode = OutputModes.StoredDefaultTerrain;
		rASResultsMap.TrySetProfile(ProfileName);
		if (!string.IsNullOrEmpty(OutputBaseFilename))
		{
			rASResultsMap.OverwriteOutputFilename = OutputBaseFilename;
		}
		using (new SetSRSHelper(terrainLayer))
		{
			if (rASResultsMap.StoreMap(prog))
			{
				ComputeSuccessful = true;
				ActualOutputFilename = rASResultsMap.SourceFilename;
			}
		}
	}

	public static StoreMapCommand Depth(string resultFilename, string pfName, string outputBaseFilename = null)
	{
		return ProfileMap(resultFilename, MapTypes.Depth, pfName, outputBaseFilename);
	}

	public static StoreMapCommand Velocity(string resultFilename, string pfName, string outputBaseFilename = null)
	{
		return ProfileMap(resultFilename, MapTypes.Velocity, pfName, outputBaseFilename);
	}

	public static StoreMapCommand DepthTimesVelocity(string resultFilename, string pfName, string outputBaseFilename = null)
	{
		return ProfileMap(resultFilename, MapTypes.DepthTimesVelocity, pfName, outputBaseFilename);
	}

	public static StoreMapCommand DepthTimesVelocitySquared(string resultFilename, string pfName, string outputBaseFilename = null)
	{
		return ProfileMap(resultFilename, MapTypes.DepthTimesVelocitySquared, pfName, outputBaseFilename);
	}

	public static StoreMapCommand WSEL(string resultFilename, string pfName, string outputBaseFilename = null)
	{
		return ProfileMap(resultFilename, MapTypes.Elevation, pfName, outputBaseFilename);
	}

	public static StoreMapCommand ProfileMap(string resultFilename, MapTypes mapType, string pfName, string outputBaseFilename = null)
	{
		return new StoreMapCommand
		{
			MapType = mapType,
			Result = resultFilename,
			ProfileName = pfName,
			OutputBaseFilename = outputBaseFilename
		};
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if ((object)MapType != null)
		{
			ele.Add(new XElement("MapType", MapType.XMLName));
		}
		if (!string.IsNullOrEmpty(Result))
		{
			ele.Add(new XElement("Result", Result));
		}
		if (!string.IsNullOrEmpty(Terrain))
		{
			ele.Add(new XElement("Terrain", Terrain));
		}
		if (!string.IsNullOrEmpty(OutputBaseFilename))
		{
			ele.Add(new XElement("OutputBaseFilename", OutputBaseFilename));
		}
		if (!string.IsNullOrEmpty(ProfileName))
		{
			ele.Add(new XElement("ProfileName", ProfileName));
		}
		if (!string.IsNullOrEmpty(ArrivalStartProfile))
		{
			ele.Add(new XElement("ArrivalStartProfile", ArrivalStartProfile));
		}
		if (!string.IsNullOrEmpty(ArrivalEndProfile))
		{
			ele.Add(new XElement("ArrivalEndProfile", ArrivalEndProfile));
		}
		if (TimeUnits != TimeUnits.None)
		{
			ele.Add(new XElement("TimeUnits", TimeUnits));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.Element("MapType");
		if (xElement != null)
		{
			MapType = MapTypes.LoadFromXMLName(xElement.Value);
		}
		XElement xElement2 = ele.Element("Result");
		if (xElement2 != null)
		{
			Result = xElement2.Value;
		}
		XElement xElement3 = ele.Element("Terrain");
		if (xElement3 != null)
		{
			Terrain = xElement3.Value;
		}
		XElement xElement4 = ele.Element("OutputBaseFilename");
		if (xElement4 != null)
		{
			OutputBaseFilename = xElement4.Value;
		}
		XElement xElement5 = ele.Element("ProfileName");
		if (xElement5 != null)
		{
			ProfileName = xElement5.Value;
		}
		XElement xElement6 = ele.Element("ArrivalStartProfile");
		if (xElement6 != null)
		{
			ArrivalStartProfile = xElement6.Value;
		}
		XElement xElement7 = ele.Element("ArrivalEndProfile");
		if (xElement7 != null)
		{
			ArrivalEndProfile = xElement7.Value;
		}
		XElement xElement8 = ele.Element("TimeUnits");
		if (xElement8 != null)
		{
			string xElement8Term = xElement8.Value;
			TimeUnits result = TimeUnits;
			Enum.TryParse<TimeUnits>(xElement8Term, out result);
			TimeUnits = result;
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		throw new NotImplementedException();
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (RASResults.TryIdentifyResultsFile(Result) == null)
		{
			prog?.ReportError("No Result found from ResultIdentifier " + Result.InSingleQuotes());
			return false;
		}
		if (MapType.NeedsProfile)
		{
			if (string.IsNullOrEmpty(ProfileName))
			{
				prog?.ReportError("ProfileName was not provided.");
			}
			return false;
		}
		return true;
	}
}
