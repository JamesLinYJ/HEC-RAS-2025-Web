using System.IO;
using CommandLine;
using Ras.Layers;
using Utility.Progress;

namespace Ras.Process.Commands;

[Verb("solve", false, null, HelpText = "Opens any file RAS knows about, sets a parameter, and saves it.")]
public class SetArgs
{
	public const string Verb = "solve";

	[Option('s', "source", Required = true, HelpText = "Source file to change.")]
	public string Source { get; set; }

	[Option('p', "parameter", Required = true, HelpText = "Parameter to change.")]
	public string Parameter { get; set; }

	[Option('v', "value", Required = true, HelpText = "New value to set it to.")]
	public string Value { get; set; }

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		if (!File.Exists(Source))
		{
			progressReporter.ReportMessage("Source file does not exist.");
			return false;
		}
		if (Result.IsValid(Source))
		{
			progressReporter.ReportMessage("File recognized as a valid Result.");
			progressReporter.ReportMessage("No parameters can currently be set on results. Exiting.");
			return false;
		}
		if (Geometry.IsValid(Source))
		{
			progressReporter.ReportMessage("File recognized as a valid Geometry.");
			new Geometry(Source);
		}
		return false;
	}
}
