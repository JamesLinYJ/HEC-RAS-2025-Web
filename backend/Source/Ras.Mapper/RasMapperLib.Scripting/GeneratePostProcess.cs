using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib.Scripting;

public class GeneratePostProcess : Command
{
	public const string sResultFilename = "ResultFilename";

	public override string CommandName { get; }

	public string ResultFilename { get; set; }

	public GeneratePostProcess()
	{
		CommandName = "GeneratePostProcess";
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
		try
		{
			new RASResults(ResultFilename).GetPostProcessor().EnsurePostProcess(prog);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			prog.ReportMessage("Error Generating Time Series Post Process file.");
			prog.ReportMessage(ex2.Message);
			prog.ReportMessage(ex2.StackTrace);
			ProjectData.ClearProjectError();
		}
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
		Execute(prog);
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
			prog.ReportError("Error: Results Filename was not specified.");
			return false;
		}
		if (!File.Exists(ResultFilename))
		{
			prog.ReportError("Error: Results Filename " + ResultFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		return true;
	}
}
