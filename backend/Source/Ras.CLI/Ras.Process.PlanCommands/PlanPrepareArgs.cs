using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Commands;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.PlanCommands;

[Verb("prepare", false, null, HelpText = "Prepare a single plan (*.h5) for compute, producing a ready-to-run (*.r2r.h5) file. Ingests external data sources, computes property tables, and bakes in any compute overrides. The parent project MUST exist with the expected directory structure - it is opened automatically for projection and associations.")]
public class PlanPrepareArgs : PlanComputeArgsBase
{
	public const string Verb = "prepare";

	[Value(0, MetaName = "source", Required = true, HelpText = "Plan file (*.h5) to prepare.")]
	public string Source { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Destination ready-to-run (*.r2r.h5) file. If not provided, writes to the plan's default ready-to-run location within the project.")]
	public string Output { get; set; }

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
			if (!SolveHelpers.TryParseIfExists(IfExists, progressReporter, out var strategy))
			{
				return false;
			}
			if (!SolveHelpers.TryValidatePlanName(Name, progressReporter, out var name))
			{
				return false;
			}
			if (!File.Exists(Source))
			{
				progressReporter.ReportMessage("Source file does not exist: " + Source.InDoubleQuotes());
				return false;
			}
			if (!Plan.IsValid(Source))
			{
				progressReporter.ReportMessage("Source is not a plan file (*.h5): " + Source.InDoubleQuotes());
				return false;
			}
			if (!SolveHelpers.TryResolvePlanInProject(Source, progressReporter, out var pl))
			{
				return false;
			}
			if (!string.IsNullOrEmpty(name))
			{
				pl.OverrideName(name);
			}
			if (!ApplyComputeOverrides(pl, progressReporter))
			{
				return false;
			}
			bool notIsNullOrWhiteSpace = !string.IsNullOrWhiteSpace(Output);
			string resolved = (notIsNullOrWhiteSpace ? Output : pl.DefaultReadyToRunFilename());
			if (!SolveHelpers.TryResolveOutputFile(resolved, strategy, notIsNullOrWhiteSpace, progressReporter, out resolved))
			{
				return false;
			}
			return SolveHelpers.TryPreparePlan(pl, resolved, progressReporter);
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}
}
