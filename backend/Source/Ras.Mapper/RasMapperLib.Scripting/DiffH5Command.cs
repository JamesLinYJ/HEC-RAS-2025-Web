using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using H5Assist.Differences;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class DiffH5Command : Command
{
	public const string CommandNameConst = "DiffH5";

	public override string CommandName { get; }

	public string BaseFilename { get; set; }

	public string NewFilename { get; set; }

	public List<string> ExtraArgs { get; set; }

	public DiffH5Command()
	{
		CommandName = "DiffH5";
		ExtraArgs = new List<string>();
	}

	public static void ExecuteWithRasProcess(string baseFilename, string testFilename)
	{
		try
		{
			string inDoubleQuotesRasProcessEXETerm = Command.RASProcessEXE.InDoubleQuotes() + " DiffH5 " + baseFilename.InDoubleQuotes() + " " + testFilename.InDoubleQuotes();
			Process.Start("cmd.exe", "/c \"" + inDoubleQuotesRasProcessEXETerm + "\" & PAUSE");
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!ValidateArguments(prog))
		{
			prog.ReportMessage("Validation failed");
		}
		else
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
		if (otherArgs.Count >= 2)
		{
			BaseFilename = otherArgs[0];
			NewFilename = otherArgs[1];
		}
		if (otherArgs.Count >= 3)
		{
			ExtraArgs = otherArgs.GetRange(2, checked(otherArgs.Count - 2)).ToList();
		}
		if (!ValidateArguments(prog))
		{
			prog.ReportMessage("Validation failed");
		}
		else
		{
			ExecuteInternal(prog);
		}
	}

	private void ExecuteInternal(ProgressReporter prog)
	{
		H5Diff.Diff(BaseFilename, NewFilename, prog, ExtraArgs);
	}

	protected override void XMLSaveTags(XElement ele)
	{
	}

	protected override void XMLLoadTags(XElement ele)
	{
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!File.Exists(BaseFilename))
		{
			prog.ReportMessage(BaseFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (!File.Exists(NewFilename))
		{
			prog.ReportMessage(NewFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		return true;
	}
}
