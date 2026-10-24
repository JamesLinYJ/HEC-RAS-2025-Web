using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib.Scripting;

public class CompleteGeometryCommand : Command
{
	public const string sGeometryFilename = "GeometryFilename";

	public override string CommandName { get; }

	public string GeometryFilename { get; set; }

	public string RasmapFilename { get; set; }

	public bool DebugMode { get; set; }

	public CompleteGeometryCommand()
	{
		CommandName = "CompleteGeometry";
		DebugMode = false;
	}

	public CompleteGeometryCommand(string geomFilename)
	{
		CommandName = "CompleteGeometry";
		DebugMode = false;
		GeometryFilename = geomFilename;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		prog.ReportMessage("Writing Geometry...");
		if (!ValidateArguments(prog))
		{
			return;
		}
		if (SharedData.SRSProjection == null)
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(GeometryFilename);
			if (Operators.CompareString(RasmapFilename, "", TextCompare: false) == 0)
			{
				RasmapFilename = Path.ChangeExtension(fileNameWithoutExtension, ".rasmap");
			}
			if (File.Exists(RasmapFilename))
			{
				SharedData.RasMapFilename = RasmapFilename;
				SharedData.SRSFilename = RasMapperCom.GetSRSFromRasmapDoc(SharedData.RasMapDoc, RasmapFilename);
			}
		}
		if (new RASGeometry(GeometryFilename).CompleteForComputations(forceRecompute: false, prog))
		{
			prog.ReportMessage("Completed Writing Geometry");
			return;
		}
		prog.ReportError("Geometry Writer Failed");
		if (DebugMode)
		{
			prog.ReportMessage("Geometry Writer command line arguments:");
			string[] commandLineArgs = Environment.GetCommandLineArgs();
			foreach (string x in commandLineArgs)
			{
				prog.ReportMessage(x.InDoubleQuotes() + " ");
			}
		}
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if (!string.IsNullOrEmpty(GeometryFilename))
		{
			ele.Add(new XElement("GeometryFilename", GeometryFilename));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.ElementCaseInsensitive("GeometryFilename");
		if (xElement != null)
		{
			GeometryFilename = xElement.Value;
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("GeomFilename"))
			{
				GeometryFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("RasMapFilename"))
			{
				RasmapFilename = kvpArg.Value;
			}
		}
		if (string.IsNullOrEmpty(GeometryFilename) | string.IsNullOrEmpty(RasmapFilename))
		{
			foreach (string otherArg in otherArgs)
			{
				if (!File.Exists(otherArg))
				{
					continue;
				}
				if (otherArg.RoughlyEndsWith(".hdf") || otherArg.RoughlyEndsWith(".h5"))
				{
					if (string.IsNullOrEmpty(GeometryFilename))
					{
						GeometryFilename = otherArg;
					}
				}
				else if (otherArg.RoughlyEndsWith(".rasmap") && string.IsNullOrEmpty(RasmapFilename))
				{
					RasmapFilename = otherArg;
				}
			}
		}
		Execute(prog);
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (string.IsNullOrEmpty(GeometryFilename))
		{
			prog.ReportError("Error: Geometry Filename was not specified.");
			return false;
		}
		if (!File.Exists(GeometryFilename))
		{
			prog.ReportError("Error: Geometry Filename " + GeometryFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		return true;
	}
}
