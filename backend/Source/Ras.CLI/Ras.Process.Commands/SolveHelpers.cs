using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.Computes;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;
using Utility.Scheduling;

namespace Ras.Process.Commands;

internal static class SolveHelpers
{
	public static bool ApplyPlanOverrides(Plan pl, int coreCount, string processor, string equationSet, string timeStepping, ProgressReporter pr)
	{
		if (coreCount < -1)
		{
			throw new Exception("Core count less than -1 not allowed.");
		}
		if (!TryParseEnumOpt(processor, "--processor", pr, out SolverType? parsed))
		{
			return false;
		}
		if (!TryParseEnumOpt(equationSet, "--equation-set", pr, out SolverControl.EquationSet? parsed2))
		{
			return false;
		}
		if (!TryParseEnumOpt(timeStepping, "--time-stepping", pr, out SolverControl.TimeStepping? parsed3))
		{
			return false;
		}
		SolverType solverType = parsed ?? pl.SolverType;
		SolverControl.EquationSet equationSet2 = parsed2 ?? pl.EquationSet;
		SolverControl.TimeStepping timeStepping2 = parsed3 ?? pl.TimeStepping;
		if (solverType == SolverType.GPU && equationSet2 == SolverControl.EquationSet.DWE)
		{
			pr.ReportMessage("Invalid solver combination: the GPU processor does not support the Diffusion Wave Equation (DWE).");
			return false;
		}
		if (solverType == SolverType.GPU && timeStepping2 == SolverControl.TimeStepping.Implicit)
		{
			pr.ReportMessage("Invalid solver combination: the GPU processor only supports explicit time-stepping.");
			return false;
		}
		if (equationSet2 == SolverControl.EquationSet.DWE && timeStepping2 == SolverControl.TimeStepping.Explicit)
		{
			pr.ReportMessage("Invalid solver combination: the Diffusion Wave Equation (DWE) only supports implicit time-stepping.");
			return false;
		}
		if (coreCount == -1)
		{
			pl.CoreCount = Environment.ProcessorCount;
		}
		else if (coreCount > 0)
		{
			pl.CoreCount = coreCount;
		}
		pl.SolverType = solverType;
		pl.EquationSet = equationSet2;
		pl.TimeStepping = timeStepping2;
		pr.ReportMessage($"Solver: {solverType} ({equationSet2}, {timeStepping2} time-stepping)");
		return true;
	}

	private static bool TryParseEnumOpt<TEnum>(string raw, string optionName, ProgressReporter pr, out TEnum? parsed) where TEnum : struct, Enum
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			parsed = null;
			return true;
		}
		if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var result) || !Enum.IsDefined(typeof(TEnum), result))
		{
			string getNamesTEnumTerm = string.Join(", ", Enum.GetNames(typeof(TEnum)));
			pr.ReportMessage("Could not parse " + optionName + " value " + raw.InDoubleQuotes() + ". Allowed values: " + getNamesTEnumTerm + ".");
			parsed = null;
			return false;
		}
		parsed = result;
		return true;
	}

	public static bool RunGraph(IEnumerable<PlanSolveRequest> requests, int maxCores, int gpuSlots, bool printGraph, ProgressReporter pr)
	{
		List<PlanSolveRequest> requestsList = requests.ToList();
		if (requestsList.Count == 0)
		{
			pr.ReportMessage("No plans to solve.");
			return false;
		}
		JobGraph jobGraph = SolveGraph.Build(requestsList);
		if (printGraph)
		{
			Console.WriteLine(jobGraph.DescribeTree());
			return true;
		}
		JobSchedulerOptions jobSchedulerOptions = new JobSchedulerOptions();
		jobSchedulerOptions.ResourceBudgets["cpu"] = ((maxCores > 0) ? maxCores : Environment.ProcessorCount);
		jobSchedulerOptions.ResourceBudgets["gpu"] = Math.Max(0, gpuSlots);
		OperationResult operationResult = new JobScheduler().Execute(jobGraph, jobSchedulerOptions, pr);
		if (!operationResult.Result)
		{
			pr.ReportMessage(operationResult.GetConcatenatedMessages());
		}
		return operationResult.Result;
	}

	public static bool TryParseIfExists(string raw, ProgressReporter pr, out OutputExistsStrategy strategy)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			strategy = OutputExistsStrategy.Default;
			return true;
		}
		if (Enum.TryParse<OutputExistsStrategy>(raw, ignoreCase: true, out strategy) && Enum.IsDefined(typeof(OutputExistsStrategy), strategy))
		{
			return true;
		}
		pr.ReportMessage("Could not parse --if-exists value " + raw.InDoubleQuotes() + ". Allowed values: force, append, error, default.");
		strategy = OutputExistsStrategy.Default;
		return false;
	}

	public static bool TryResolveOutputFile(string targetFn, OutputExistsStrategy strategy, bool outputWasExplicit, ProgressReporter pr, out string resolved)
	{
		resolved = targetFn;
		if (strategy == OutputExistsStrategy.Default)
		{
			strategy = (outputWasExplicit ? OutputExistsStrategy.Error : OutputExistsStrategy.Append);
		}
		if (strategy == OutputExistsStrategy.Append)
		{
			resolved = FileHelpers.UniqueFilename(targetFn);
			return true;
		}
		if (!File.Exists(targetFn))
		{
			return true;
		}
		if (strategy == OutputExistsStrategy.Force)
		{
			pr.ReportMessage("Output file " + targetFn.InDoubleQuotes() + " already exists, deleting...");
			File.Delete(targetFn);
			return true;
		}
		pr.ReportMessage("Output file " + targetFn.InDoubleQuotes() + " exists. Rerun with '--if-exists force' to overwrite it, or '--if-exists append' to write a numbered copy.");
		return false;
	}

	public static bool TryValidatePlanName(string raw, ProgressReporter pr, out string name)
	{
		name = null;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return true;
		}
		string trim = raw.Trim();
		if (trim.Length > 64)
		{
			pr.ReportMessage("--name is too long (" + trim.Length + " characters). Maximum is " + 64 + ".");
			return false;
		}
		int indexOfAny = trim.IndexOfAny(CoreLayerBase.IllegalNameCharacters);
		if (indexOfAny >= 0)
		{
			pr.ReportMessage("--name contains an invalid character: " + trim[indexOfAny].ToString().InDoubleQuotes() + ".");
			return false;
		}
		name = trim;
		return true;
	}

	public static Project TryOpenProject(string source, ProgressReporter pr)
	{
		return CliHelpers.TimeStep("Opening Project...", () =>
		{
			OperationResult operationResult = Project.TryLoad(source, out var prj);
			if (!operationResult)
			{
				pr.ReportMessage(operationResult.GetConcatenatedMessages());
			}
			return prj;
		});
	}

	public static bool TryResolvePlanInProject(string planSource, ProgressReporter pr, out Plan pl)
	{
		pl = null;
		string getParentDirectoryTerm = Directory.GetParent(Path.GetDirectoryName(planSource))?.FullName;
		if (string.IsNullOrEmpty(getParentDirectoryTerm) || !Directory.Exists(getParentDirectoryTerm))
		{
			pr.ReportMessage("Could not find parent project path for plan: " + planSource.InDoubleQuotes());
			return false;
		}
		Project project = TryOpenProject(getParentDirectoryTerm, pr);
		if (project == null)
		{
			return false;
		}
		string desiredFullPath = Path.GetFullPath(planSource);
		pl = project.Plans.FirstOrDefault((Plan p) => Path.GetFullPath(p.Filename).RoughlyEquals(desiredFullPath));
		if (pl == null)
		{
			pr.ReportMessage("Plan " + planSource.InDoubleQuotes() + " not found in project " + getParentDirectoryTerm.InDoubleQuotes() + ".");
			return false;
		}
		return true;
	}

	public static bool TryPreparePlan(Plan pl, string targetFn, ProgressReporter pr)
	{
		OperationResult operationResult = pl.TryPrepareCompute(targetFn, out var _, pr);
		if (!operationResult)
		{
			pr.ReportMessage("Plan " + pl.Filename.InDoubleQuotes() + " failed to prepare for compute.");
			pr.ReportMessage(operationResult.GetConcatenatedMessages());
			return false;
		}
		return true;
	}
}
