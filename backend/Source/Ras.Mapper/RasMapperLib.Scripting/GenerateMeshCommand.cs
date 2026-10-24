using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class GenerateMeshCommand : Command
{
	public const string sPerimeterShpfile = "PerimeterFilename";

	public const string sGeometryFile = "GeometryFilename";

	public const string sCellSize = "CellSize";

	public const string sName = "Name";

	public string PerimeterFilename { get; set; }

	public string GeometryFilename { get; set; }

	public float CellSize { get; set; }

	public string MeshName { get; set; }

	public override string CommandName { get; }

	public GenerateMeshCommand()
	{
		CommandName = "GenerateMesh";
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (ValidateArguments(prog))
		{
			ExecuteInternal(prog);
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

	private bool ExecuteInternal(ProgressReporter prog)
	{
		throw new NotImplementedException();
	}

	protected override void XMLSaveTags(XElement ele)
	{
		throw new NotImplementedException();
	}

	protected override void XMLLoadTags(XElement ele)
	{
		throw new NotImplementedException();
	}

	private void ParseImportArgs(Dictionary<string, string> kvpArgs)
	{
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("GeometryFilename"))
			{
				GeometryFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("PerimeterFilename"))
			{
				PerimeterFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("CellSize"))
			{
				string kvpArgTerm = kvpArg.Value;
				float result = CellSize;
				float.TryParse(kvpArgTerm, out result);
				CellSize = result;
			}
			else if (kvpArg.Key.RoughlyEquals("Name"))
			{
				MeshName = kvpArg.Value;
			}
		}
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (string.IsNullOrEmpty(PerimeterFilename))
		{
			prog.ReportError("PerimeterFilename".InSingleQuotes() + " not defined.");
			return false;
		}
		if (!File.Exists(PerimeterFilename))
		{
			prog.ReportError(PerimeterFilename.InSingleQuotes() + " does not exist.");
			return false;
		}
		if (string.IsNullOrEmpty(GeometryFilename))
		{
			prog.ReportError("GeometryFilename".InSingleQuotes() + " not defined.");
			return false;
		}
		if (!File.Exists(GeometryFilename))
		{
			prog.ReportError(GeometryFilename.InSingleQuotes() + " does not exist - please create one with the 'CreateGeometry' command.");
			return false;
		}
		if (CellSize <= 0f || float.IsNaN(CellSize))
		{
			prog.ReportError("Inappropriate Cell Size: " + CellSize);
			return false;
		}
		if (string.IsNullOrEmpty(MeshName))
		{
			prog.ReportError("Name".InSingleQuotes() + " not provided.");
			return false;
		}
		return true;
	}
}
