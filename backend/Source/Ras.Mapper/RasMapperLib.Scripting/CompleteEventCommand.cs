using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using H5Assist.VB;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib.Scripting;

public class CompleteEventCommand : Command
{
	public const string sResultFilename = "ResultFilename";

	public const string CommandNameConst = "CompleteEventConditions";

	public override string CommandName { get; }

	public string ResultFilename { get; set; }

	public bool DebugMode { get; set; }

	public string Units { get; set; }

	public CompleteEventCommand()
	{
		CommandName = "CompleteEventConditions";
		DebugMode = false;
		Units = "";
	}

	public CompleteEventCommand(string resFilename)
	{
		CommandName = "CompleteEventConditions";
		DebugMode = false;
		Units = "";
		ResultFilename = resFilename;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!ValidateArguments(prog))
		{
			prog.ReportMessage("Error executing: " + CommandName);
			return;
		}
		prog.ReportMessage("Writing Event Conditions ...");
		if (SharedData.SRSProjection == null)
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(ResultFilename);
			if (fileNameWithoutExtension.RoughlyEndsWith(".tmp"))
			{
				fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileNameWithoutExtension);
			}
			string path = Path.Combine(Path.GetDirectoryName(ResultFilename), Path.ChangeExtension(fileNameWithoutExtension, ".rasmap"));
			if (File.Exists(path))
			{
				SharedData.RasMapFilename = path;
				SharedData.SRSFilename = RasMapperCom.GetSRSFromRasmapDoc(SharedData.RasMapDoc, path);
			}
		}
		if (!string.IsNullOrEmpty(Units))
		{
			SharedData.SetUnitsSystem(Units);
		}
		if (new RASResults(ResultFilename).EventConditions.CompleteForComputations(prog))
		{
			prog.ReportMessage("Completed Writing Event Condition Data");
		}
		H5Writer.Squeeze(ResultFilename);
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if (!string.IsNullOrEmpty(ResultFilename))
		{
			ele.Add(new XElement("ResultFilename", ResultFilename));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.ElementCaseInsensitive("ResultFilename");
		if (xElement != null)
		{
			ResultFilename = xElement.Value;
		}
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (string.IsNullOrEmpty(ResultFilename))
		{
			prog?.ReportError("No Result Filename set.");
			return false;
		}
		if (!File.Exists(ResultFilename))
		{
			prog?.ReportError("Result Filename does not exist: " + ResultFilename.InDoubleQuotes());
			return false;
		}
		return true;
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		string resultFilename = "";
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("ResultFilename"))
			{
				resultFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("Units"))
			{
				Units = kvpArg.Value;
			}
		}
		if (string.IsNullOrEmpty(resultFilename))
		{
			foreach (string otherArg in otherArgs)
			{
				if (File.Exists(otherArg) && (otherArg.RoughlyEndsWith(".hdf") || otherArg.RoughlyEndsWith(".h5")))
				{
					resultFilename = otherArg;
					break;
				}
			}
		}
		ResultFilename = resultFilename;
		if (ValidateArguments(prog))
		{
			Execute(prog);
		}
	}
}
