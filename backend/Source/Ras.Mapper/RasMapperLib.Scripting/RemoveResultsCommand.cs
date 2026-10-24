using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib.Scripting;

public class RemoveResultsCommand : Command
{
	public const string sSource = "Source";

	public const string sDestination = "Destination";

	public override string CommandName { get; }

	public string SourceFilename { get; set; }

	public string DestinationFilename { get; set; }

	public RemoveResultsCommand()
	{
		CommandName = "RemoveResults";
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (!ValidateArguments(prog))
		{
			prog.ReportMessage("Error executing: " + CommandName);
			return;
		}
		prog.ReportMessage("Removing Results from file ...");
		try
		{
			using (H5Reader h5Reader = new H5Reader(SourceFilename))
			{
				List<string> groupNames = h5Reader.GetGroupNames("/");
				using H5Writer writer = new H5Writer(DestinationFilename);
				foreach (string groupName in groupNames)
				{
					if (Operators.CompareString(groupName, "Results", TextCompare: false) != 0)
					{
						h5Reader.CopyTo(writer, groupName, groupName);
					}
				}
				h5Reader.CopyAttributesTo(writer, "/", "/");
			}
			if (Operators.CompareString(Path.GetDirectoryName(SourceFilename).ToLower(), Path.GetDirectoryName(DestinationFilename).ToLower(), TextCompare: false) != 0)
			{
				RASGeometry.UpdateAssociatedFilenames(SourceFilename, DestinationFilename);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("Source"))
			{
				SourceFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("Destination"))
			{
				DestinationFilename = kvpArg.Value;
			}
		}
		if ((string.IsNullOrEmpty(SourceFilename) | string.IsNullOrEmpty(DestinationFilename)) && otherArgs.Count == 2)
		{
			SourceFilename = otherArgs.First();
			DestinationFilename = otherArgs.Last();
		}
		Execute(prog);
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if (!string.IsNullOrEmpty(SourceFilename))
		{
			ele.Add(new XElement("Source", SourceFilename));
		}
		if (!string.IsNullOrEmpty(DestinationFilename))
		{
			ele.Add(new XElement("Destination", DestinationFilename));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.ElementCaseInsensitive("Source");
		if (xElement != null)
		{
			SourceFilename = xElement.Value;
		}
		XElement xElement2 = ele.ElementCaseInsensitive("Destination");
		if (xElement2 != null)
		{
			DestinationFilename = xElement2.Value;
		}
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (!string.IsNullOrEmpty(SourceFilename) && File.Exists(SourceFilename) && !string.IsNullOrEmpty(DestinationFilename) && Operators.CompareString(SourceFilename.ToLower(), DestinationFilename.ToLower(), TextCompare: false) != 0)
		{
			return FileHelpers.IsValidPath(DestinationFilename);
		}
		return false;
	}
}
