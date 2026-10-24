using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class LaunchRasMapperCommand : Command
{
	public override string CommandName { get; }

	public List<string> Files { get; set; }

	public LaunchRasMapperCommand()
	{
		CommandName = "LaunchRasMapper";
		Files = new List<string>();
	}

	public override void Execute(ProgressReporter prog = null)
	{
		Process process = new Process();
		process.StartInfo = new ProcessStartInfo("C:\\Programs\\6.x Development\\rasmapper.exe", string.Join(" ", Files.Select([SpecialName] (string textValue) => textValue.InDoubleQuotes())))
		{
			CreateNoWindow = true,
			UseShellExecute = false
		};
		process.Start();
	}

	public static void LaunchWith(IEnumerable<string> files)
	{
		LaunchRasMapperCommand launchRasMapperCommand = new LaunchRasMapperCommand();
		launchRasMapperCommand.Files.AddRange(files);
		launchRasMapperCommand.Execute();
	}

	protected override void XMLSaveTags(XElement ele)
	{
		foreach (string file in Files)
		{
			ele.Add("File", file);
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		foreach (XElement element in ele.Elements("File"))
		{
			Files.Add(element.Value);
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		throw new NotImplementedException();
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		return true;
	}
}
