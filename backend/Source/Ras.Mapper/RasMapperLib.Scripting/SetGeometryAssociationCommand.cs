using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class SetGeometryAssociationCommand : Command
{
	public const string sGeometryFile = "GeometryFilename";

	public const string sTerrainFile = "TerrainFilename";

	public const string sNValueLCFile = "NValueFilename";

	public const string sInfiltrationLCFile = "InfiltrationFilename";

	public const string sSedimentSoilsLCFile = "SedimentSoilsFilename";

	public const string sCommandName = "SetGeometryAssociation";

	public override string CommandName { get; }

	public string GeometryFilename { get; set; }

	public string TerrainFilename { get; set; }

	public string NValueFilename { get; set; }

	public string InfiltrationFilename { get; set; }

	public string SedimentSoilsFilename { get; set; }

	public SetGeometryAssociationCommand()
	{
		CommandName = "SetGeometryAssociation";
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (ValidateArguments(prog))
		{
			try
			{
				ExecuteInternal(prog);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				prog.ReportError(ex2.Message);
				ProjectData.ClearProjectError();
			}
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		ParseImportArgs(kvpArgs);
		if (ValidateArguments(prog))
		{
			try
			{
				ExecuteInternal(prog);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				prog.ReportError(ex2.Message);
				ProjectData.ClearProjectError();
			}
		}
	}

	private void ExecuteInternal(ProgressReporter prog)
	{
		RASGeometry rASGeometry = new RASGeometry(GeometryFilename);
		if (!string.IsNullOrEmpty(TerrainFilename))
		{
			TerrainLayer terrain = new TerrainLayer(Path.GetFileNameWithoutExtension(TerrainFilename), TerrainFilename);
			rASGeometry.Terrain = terrain;
			prog.ReportMessage("Terrain association set to " + TerrainFilename.InSingleQuotes());
		}
		if (!string.IsNullOrEmpty(NValueFilename))
		{
			LandCoverLayer nValueLayer = new LandCoverLayer(NValueFilename);
			rASGeometry.NValueLayer = nValueLayer;
			prog.ReportMessage("N-Value association set to " + NValueFilename.InSingleQuotes());
		}
		if (!string.IsNullOrEmpty(InfiltrationFilename))
		{
			LandCoverLayer infiltrationLayer = new LandCoverLayer(InfiltrationFilename);
			rASGeometry.InfiltrationLayer = infiltrationLayer;
			prog.ReportMessage("Infiltration association set to " + InfiltrationFilename.InSingleQuotes());
		}
		if (!string.IsNullOrEmpty(SedimentSoilsFilename))
		{
			LandCoverLayer sedimentSoilsLayer = new LandCoverLayer(SedimentSoilsFilename);
			rASGeometry.SedimentSoilsLayer = sedimentSoilsLayer;
			prog.ReportMessage("Sediment-soils association set to " + SedimentSoilsFilename.InSingleQuotes());
		}
	}

	private void ParseImportArgs(Dictionary<string, string> kvpArgs)
	{
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("GeometryFilename"))
			{
				GeometryFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("TerrainFilename"))
			{
				TerrainFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("NValueFilename"))
			{
				NValueFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("InfiltrationFilename"))
			{
				InfiltrationFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("SedimentSoilsFilename"))
			{
				SedimentSoilsFilename = kvpArg.Value;
			}
		}
	}

	protected override void XMLSaveTags(XElement ele)
	{
		throw new NotImplementedException();
	}

	protected override void XMLLoadTags(XElement ele)
	{
		throw new NotImplementedException();
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (string.IsNullOrEmpty(GeometryFilename))
		{
			prog.ReportError("GeometryFilename".InSingleQuotes() + " not defined.");
			return false;
		}
		if (!File.Exists(GeometryFilename))
		{
			prog.ReportError(GeometryFilename.InSingleQuotes() + " does not exist.");
			return false;
		}
		if (!string.IsNullOrEmpty(TerrainFilename) && !File.Exists(TerrainFilename))
		{
			prog.ReportError("File " + TerrainFilename.InSingleQuotes() + " does not exist.");
			return false;
		}
		if (!string.IsNullOrEmpty(TerrainFilename) && !File.Exists(TerrainFilename))
		{
			prog.ReportError("File " + TerrainFilename.InSingleQuotes() + " does not exist.");
			return false;
		}
		if (!string.IsNullOrEmpty(NValueFilename) && !File.Exists(NValueFilename))
		{
			prog.ReportError("File " + NValueFilename.InSingleQuotes() + " does not exist.");
			return false;
		}
		if (!string.IsNullOrEmpty(InfiltrationFilename) && !File.Exists(InfiltrationFilename))
		{
			prog.ReportError("File " + InfiltrationFilename.InSingleQuotes() + " does not exist.");
			return false;
		}
		if (!string.IsNullOrEmpty(SedimentSoilsFilename) && !File.Exists(SedimentSoilsFilename))
		{
			prog.ReportError("File " + SedimentSoilsFilename.InSingleQuotes() + " does not exist.");
			return false;
		}
		return true;
	}
}
