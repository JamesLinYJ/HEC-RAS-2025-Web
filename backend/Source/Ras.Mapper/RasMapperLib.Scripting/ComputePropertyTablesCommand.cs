using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class ComputePropertyTablesCommand : Command
{
	public override string CommandName { get; }

	public string Geometry { get; set; }

	public ComputePropertyTablesCommand()
	{
		CommandName = "ComputePropertyTables";
	}

	public ComputePropertyTablesCommand(string geomFile)
	{
		CommandName = "ComputePropertyTables";
		Geometry = geomFile;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		new RASGeometry(Geometry).D2FlowArea.EnsurePropertyTables(forceRecompute: true);
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if (!string.IsNullOrEmpty(Geometry))
		{
			ele.Add(new XElement("Geometry", Geometry));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.Element("Geometry");
		if (xElement != null)
		{
			Geometry = xElement.Value;
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		throw new NotImplementedException();
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (File.Exists(Geometry))
		{
			return true;
		}
		prog?.ReportError(Geometry.InSingleQuotes() + " does not exist.");
		return false;
	}
}
