using System;
using System.IO;
using CommandLine;
using Ras.Layers;
using Ras.Process.PlanCommands;
using Ras.Process.ProjectCommands;
using Utility.Extensions;

namespace Ras.Process.Commands;

[Verb("prepare", false, null, HelpText = "Prepare a project or plan for compute. Ingests all external data sources, computes property tables, and does any other preprocessing necessary, producing ready-to-run (*.r2r.h5) file(s) that can be computed with 'ras solve'. Auto-detects the source kind and forwards to 'ras project prepare' or 'ras plan prepare' - use those commands directly when you need their tuning options.")]
public class PrepareArgs
{
	public const string Verb = "prepare";

	[Value(0, MetaName = "source", Required = true, HelpText = "Source to prepare. This goes through a multi-step resolution process.\n\tIf the given source is a directory or project file (*.ras), forwards to 'ras project prepare' with default options.\n\tIf the given source is a plan file (*.h5), forwards to 'ras plan prepare' with default options.")]
	public string Source { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Output file or directory to write the prepared result.\n\tIf unspecified, ready-to-run file(s) will be written to the locations specified in 'ras plan prepare' or 'ras project prepare'.")]
	public string Output { get; set; }

	[Option("if-exists", Required = false, HelpText = "What to do when an output file already exists. See 'ras plan prepare' / 'ras project prepare' for the per-strategy details.\n\t'force': delete the existing file and overwrite it.\n\t'append': write to a numbered copy instead (e.g. 'output (3).r2r.h5').\n\t'error': report a message and refuse to overwrite.\n\t'default' (default): error when you name the output explicitly, append when it's auto-derived.")]
	public string IfExists { get; set; }

	public bool Execute()
	{
		bool exists = File.Exists(Source);
		if (Directory.Exists(Source) || (exists && (bool)Project.IsValidProjectFile(Source)))
		{
			return new ProjectPrepareArgs
			{
				Source = Source,
				Output = Output,
				IfExists = IfExists
			}.Execute();
		}
		if (exists && Plan.IsValid(Source))
		{
			return new PlanPrepareArgs
			{
				Source = Source,
				Output = Output,
				IfExists = IfExists
			}.Execute();
		}
		Console.WriteLine("Could not parse source: " + Source.InDoubleQuotes());
		return false;
	}
}
