using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class ExportGeometryCommand : Command
{
	public override string CommandName { get; }

	public ExportGeometryCommand()
	{
		CommandName = "ExportGeometry";
	}

	public override void Execute(ProgressReporter prog = null)
	{
		throw new NotImplementedException();
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
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

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		throw new NotImplementedException();
	}
}
