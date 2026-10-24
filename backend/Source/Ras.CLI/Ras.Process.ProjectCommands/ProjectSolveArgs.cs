using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Layers.Computes;
using Ras.Process.Commands;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.ProjectCommands;

[Verb("solve", false, null, HelpText = "Run the RAS solver on every plan in a project.")]
public class ProjectSolveArgs
{
	public const string Verb = "solve";

	[Value(0, MetaName = "source", Required = true, HelpText = "Project file (*.ras) or directory containing one. Every plan in the project will be solved.")]
	public string Source { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Destination directory for the solved result files. If unspecified, writes to the default location in the project.")]
	public string Output { get; set; }

	[Option("if-exists", Required = false, HelpText = "What to do when a plan's result file already exists:\n\t'force': delete the existing file and overwrite it.\n\t'append': write to a numbered copy instead (e.g. 'Plan (1).h5').\n\t'error': report a message and skip that plan.\n\t'default' (default): append a numbered copy (plan result filenames are always auto-derived here).")]
	public string IfExists { get; set; }

	[Option("max-cores", Required = false, HelpText = "Total CPU core budget the scheduler is allowed to use across all in-flight plans. Default (0) uses all logical processors.")]
	public int MaxCores { get; set; }

	[Option("gpu-slots", Required = false, HelpText = "Number of GPU plans allowed to run concurrently. Defaults to 1.")]
	public int GpuSlots { get; set; } = 1;

	[Option("print-graph", Required = false, HelpText = "Print the solve dependency graph instead of running it.")]
	public bool PrintGraph { get; set; }

	public bool Execute()
	{
		try
		{
			if (string.IsNullOrWhiteSpace(Source))
			{
				Console.WriteLine("Source is required.");
				return false;
			}
			CliHelpers.TimeStep("Initializing GDAL...", () =>
			{
				GDALSetup.InitializeMultiplatform();
			});
			ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
			bool exists = File.Exists(Source);
			if (!Directory.Exists(Source) && (!exists || !Project.IsValidProjectFile(Source)))
			{
				progressReporter.ReportMessage("Source is not a project file (*.ras) or project directory: " + Source.InDoubleQuotes());
				return false;
			}
			if (!SolveHelpers.TryParseIfExists(IfExists, progressReporter, out var strategy))
			{
				return false;
			}
			Project project = SolveHelpers.TryOpenProject(Source, progressReporter);
			if (project == null)
			{
				return false;
			}
			List<PlanSolveRequest> planSolveRequests = new List<PlanSolveRequest>();
			foreach (Plan plan in project.Plans)
			{
				string resolved = plan.DefaultResultFilename();
				if (!string.IsNullOrEmpty(Output))
				{
					resolved = Path.Combine(Output, Path.GetFileName(resolved));
				}
				if (SolveHelpers.TryResolveOutputFile(resolved, strategy, outputWasExplicit: false, progressReporter, out resolved))
				{
					planSolveRequests.Add(new PlanSolveRequest(plan, resolved));
				}
			}
			return SolveHelpers.RunGraph(planSolveRequests, MaxCores, GpuSlots, PrintGraph, progressReporter);
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}
}
