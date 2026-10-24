using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class MergePolygonCommand : Command
{
	public override string CommandName { get; }

	public string PolygonBinaryFile { get; set; }

	public MergePolygonCommand()
	{
		CommandName = "MergePolygon";
	}

	public MergePolygonCommand(string xyBinFile)
	{
		CommandName = "MergePolygon";
		PolygonBinaryFile = xyBinFile;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		ContourBandRaster.ExternalProcessMerge(PolygonBinaryFile);
	}

	public static void Execute(string xyBinFile, ProgressReporter prog = null)
	{
		new MergePolygonCommand(xyBinFile).Execute(prog);
	}

	public static void ExecuteOutOfProcess(string xyBinFile)
	{
		new MergePolygonCommand(xyBinFile).ExecuteOutOfProcess(showWindow: false);
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if (!string.IsNullOrEmpty(PolygonBinaryFile))
		{
			ele.Add(new XElement("PolygonBinaryFile", PolygonBinaryFile));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.Element("PolygonBinaryFile");
		if (xElement != null)
		{
			PolygonBinaryFile = xElement.Value;
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		throw new NotImplementedException();
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (!File.Exists(PolygonBinaryFile))
		{
			prog?.ReportError(PolygonBinaryFile.InSingleQuotes() + " does not exist.");
			return false;
		}
		return true;
	}
}
