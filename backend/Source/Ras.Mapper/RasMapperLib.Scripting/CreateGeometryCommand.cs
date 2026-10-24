using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class CreateGeometryCommand : Command
{
	public const string sGeometryFile = "GeometryFilename";

	public const string sGeometryTitle = "Title";

	public const string sUnitSystem = "UnitSystem";

	public const string sCommandName = "CreateGeometry";

	public override string CommandName { get; }

	public string GeometryFilename { get; set; }

	public string GeometryTitle { get; set; }

	public SharedData.EnumUnits UnitSystem { get; set; }

	public CreateGeometryCommand()
	{
		CommandName = "CreateGeometry";
		UnitSystem = SharedData.EnumUnits.USCustomary;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (ValidateArguments(prog))
		{
			Compute(prog);
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
			Compute(prog);
		}
	}

	private void Compute(ProgressReporter prog)
	{
		RASGeometry.CreateEmpty(GeometryFilename, GeometryTitle, UnitSystem);
	}

	private void ParseImportArgs(Dictionary<string, string> kvpArgs)
	{
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("GeometryFilename"))
			{
				GeometryFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("Title"))
			{
				GeometryTitle = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("UnitSystem"))
			{
				if (kvpArg.Value.RoughlyEquals("si"))
				{
					UnitSystem = SharedData.EnumUnits.SIUnits;
				}
				else if (kvpArg.Value.RoughlyEquals("usc") || kvpArg.Value.RoughlyEquals("uscustomary"))
				{
					UnitSystem = SharedData.EnumUnits.USCustomary;
				}
				else
				{
					UnitSystem = SharedData.EnumUnits.USCustomary;
				}
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
		if (File.Exists(GeometryFilename))
		{
			prog.ReportError(GeometryFilename.InSingleQuotes() + " already exists - please delete the file if you intend to overwrite it.");
			return false;
		}
		if (string.IsNullOrEmpty(GeometryTitle))
		{
			string geometryTitle = "Geometry Title";
			prog.ReportWarning("Title".InSingleQuotes() + " not defined, using '" + geometryTitle + "'");
			GeometryTitle = geometryTitle;
		}
		return true;
	}
}
