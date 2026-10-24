using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Layers.Computes;
using Ras.Process.Commands;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.PlanCommands;

[Verb("solve", false, null, HelpText = "Run the RAS solver on a single plan (*.h5) or ready-to-run result (*.r2r.h5).")]
public class PlanSolveArgs : PlanComputeArgsBase
{
	public const string Verb = "solve";

	[Value(0, MetaName = "source", Required = true, HelpText = "Plan file (*.h5) or ready-to-run result (*.r2r.h5) to solve.\n\tFor a plan file, the parent project MUST exist with the expected directory structure, and is opened automatically for projection and associations.\n\tFor a ready-to-run (*.r2r.h5) file, the project does not have to exist.")]
	public string Source { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Destination result (*.h5) file. If not provided, output filename will be assumed based on the file type.\n\tFor a plan file, the output filename will go to ../Results/[PlanName].h5, with an automatic count appended for file duplicates (e.g. '../Results/Plan (4).h5').\n\tFor a ready-to-run file, the output filename will be in the same directory, changing the 'r2r' for 'res' (e.g. './[BaseName].res.h5').")]
	public string Output { get; set; }

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
			if (Source.RoughlyEndsWith(".r2r.h5"))
			{
				return SolveReadyToRun(progressReporter, strategy, name);
			}
			if (Plan.IsValid(Source))
			{
				return SolvePlanInProject(progressReporter, strategy, name);
			}
			progressReporter.ReportMessage("Source is not a plan file (*.h5) or ready-to-run result (*.r2r.h5): " + Source.InDoubleQuotes());
			return false;
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}

	private bool SolveReadyToRun(ProgressReporter pr, OutputExistsStrategy ifExists, string planName)
	{
		bool notIsNullOrWhiteSpace = !string.IsNullOrWhiteSpace(Output);
		string resolved = (notIsNullOrWhiteSpace ? Output : DefaultReadyToRunResultFilename(Source));
		if (!SolveHelpers.TryResolveOutputFile(resolved, ifExists, notIsNullOrWhiteSpace, pr, out resolved))
		{
			return false;
		}
		File.Copy(Source, resolved);
		Result result = new Result(resolved);
		if (!string.IsNullOrEmpty(planName))
		{
			result.RenameEmbeddedPlan(planName);
		}
		if (!ApplyComputeOverrides(result.Plan, pr))
		{
			return false;
		}
		Action<Action> synchronizationContext = (Action a) =>
		{
			a();
		};
		OperationResult operationResult = result.Compute(synchronizationContext, pr);
		if (!operationResult.Result)
		{
			pr.ReportMessage(operationResult.GetConcatenatedMessages());
		}
		return operationResult.Result;
	}

	private bool SolvePlanInProject(ProgressReporter pr, OutputExistsStrategy ifExists, string planName)
	{
		if (!SolveHelpers.TryResolvePlanInProject(Source, pr, out var pl))
		{
			return false;
		}
		string resolved = pl.DefaultResultFilename();
		bool notIsNullOrEmpty = !string.IsNullOrEmpty(Output);
		if (notIsNullOrEmpty)
		{
			resolved = Output;
		}
		if (!string.IsNullOrEmpty(planName))
		{
			pl.OverrideName(planName);
		}
		if (!ApplyComputeOverrides(pl, pr))
		{
			return false;
		}
		if (!SolveHelpers.TryResolveOutputFile(resolved, ifExists, notIsNullOrEmpty, pr, out resolved))
		{
			return false;
		}
		return SolveHelpers.RunGraph(new PlanSolveRequest[1]
		{
			new PlanSolveRequest(pl, resolved)
		}, 0, 1, PrintGraph, pr);
	}

	private static string DefaultReadyToRunResultFilename(string source)
	{
		string fileName = Path.GetFileName(source);
		if (fileName.RoughlyEndsWith(".r2r.h5"))
		{
			fileName = fileName.Substring(0, fileName.Length - ".r2r.h5".Length);
		}
		return Path.Combine(Path.GetDirectoryName(source) ?? "", fileName + ".res.h5");
	}
}
