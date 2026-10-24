using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class LoadSaveGeometryCommand : Command
{
	public string GeometryFilename;

	public const string CommandNameConst = "LoadSaveGeometry";

	public override string CommandName { get; }

	public LoadSaveGeometryCommand()
	{
		CommandName = "LoadSaveGeometry";
	}

	public LoadSaveGeometryCommand(string geomFilename)
	{
		CommandName = "LoadSaveGeometry";
		GeometryFilename = geomFilename;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		new RASGeometry(GeometryFilename).LoadSaveAllLayers();
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if (!string.IsNullOrEmpty(GeometryFilename))
		{
			ele.Add(new XElement("Filename", GeometryFilename));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.Element("Filename");
		if (xElement != null)
		{
			GeometryFilename = xElement.Value;
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		string geometryFilename = "";
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("Filename"))
			{
				geometryFilename = kvpArg.Value;
			}
		}
		if (string.IsNullOrEmpty(geometryFilename))
		{
			foreach (string otherArg in otherArgs)
			{
				if (File.Exists(otherArg))
				{
					geometryFilename = otherArg;
					break;
				}
			}
		}
		GeometryFilename = geometryFilename;
		if (ValidateArguments(prog))
		{
			Execute(prog);
		}
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		if (string.IsNullOrEmpty(GeometryFilename))
		{
			return false;
		}
		if (!File.Exists(GeometryFilename))
		{
			return false;
		}
		return true;
	}
}
