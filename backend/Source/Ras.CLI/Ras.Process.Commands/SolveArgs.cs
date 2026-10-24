using System;
using System.IO;
using CommandLine;
using Ras.Layers;
using Ras.Process.PlanCommands;
using Ras.Process.ProjectCommands;
using Utility.Extensions;

namespace Ras.Process.Commands;

[Verb("solve", false, null, HelpText = "Run the RAS solver on a project, plan, or ready-to-run result. Auto-detects the source kind and forwards to 'ras project solve' or 'ras plan solve' - use those commands directly when you need their tuning options.")]
public class SolveArgs
{
	public const string Verb = "solve";

	[Value(0, MetaName = "source", Required = true, HelpText = "Source to solve. This goes through a multi-step resolution process.\n\tIf the given source is a directory or project file (*.ras), forwards to 'ras project solve' with default options.\n\tIf the given source is a plan file (*.h5) or ready-to-run (*.r2r.h5) file, forwards to 'ras plan solve' with default options.")]
	public string Source { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Output file or directory to write the solved result.\n\tIf unspecified, solved result file(s) will be written to the locations specified in 'ras plan solve' or 'ras project solve'.")]
	public string Output { get; set; }

	[Option("if-exists", Required = false, HelpText = "What to do when an output file already exists. See 'ras plan solve' / 'ras project solve' for the per-strategy details.\n\t'force': delete the existing file and overwrite it.\n\t'append': write to a numbered copy instead (e.g. 'output (3).h5').\n\t'error': report a message and refuse to overwrite.\n\t'default' (default): error when you name the output explicitly, append when it's auto-derived.")]
	public string IfExists { get; set; }

	public bool Execute()
	{
		bool exists = File.Exists(Source);
		if (Directory.Exists(Source) || (exists && (bool)Project.IsValidProjectFile(Source)))
		{
			return new ProjectSolveArgs
			{
				Source = Source,
				Output = Output,
				IfExists = IfExists
			}.Execute();
		}
		if (exists && (Source.RoughlyEndsWith(".r2r.h5") || Plan.IsValid(Source)))
		{
			return new PlanSolveArgs
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
