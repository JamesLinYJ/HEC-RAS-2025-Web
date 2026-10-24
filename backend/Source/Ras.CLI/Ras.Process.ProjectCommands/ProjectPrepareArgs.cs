using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Commands;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.ProjectCommands;

[Verb("prepare", false, null, HelpText = "Prepare every plan in a project for compute, producing a ready-to-run (*.r2r.h5) file per plan. Ingests external data sources and computes property tables so the results can be solved with 'ras solve'.")]
public class ProjectPrepareArgs
{
	public const string Verb = "prepare";

	[Value(0, MetaName = "source", Required = true, HelpText = "Project file (*.ras) or directory containing one. Every plan in the project will be prepared.")]
	public string Source { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Destination directory for the ready-to-run files. If unspecified, writes to each plan's default location in the project.")]
	public string Output { get; set; }

	[Option("if-exists", Required = false, HelpText = "What to do when a plan's ready-to-run file already exists:\n\t'force': delete the existing file and overwrite it.\n\t'append': write to a numbered copy instead (e.g. 'Plan (1).r2r.h5').\n\t'error': report a message and skip that plan.\n\t'default' (default): append a numbered copy (ready-to-run filenames are always auto-derived here).")]
	public string IfExists { get; set; }

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
			bool condition = false;
			foreach (Plan plan in project.Plans)
			{
				ProgressReporter progressReporter2 = progressReporter.SubTask("Preparing Plan", 0f, 1f);
				progressReporter2.ReportMessage("Preparing Plan " + plan.Name.InDoubleQuotes() + "...");
				string resolved = plan.DefaultReadyToRunFilename();
				if (!string.IsNullOrEmpty(Output))
				{
					resolved = Path.Combine(Output, Path.GetFileName(resolved));
				}
				if (!SolveHelpers.TryResolveOutputFile(resolved, strategy, outputWasExplicit: false, progressReporter2, out resolved))
				{
					condition = true;
				}
				else if (!SolveHelpers.TryPreparePlan(plan, resolved, progressReporter2))
				{
					condition = true;
				}
				progressReporter2.ReportMessage("");
			}
			return !condition;
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}
}
