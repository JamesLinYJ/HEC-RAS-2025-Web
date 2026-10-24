using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using Ras.Layers;
using Ras.Layers.Cleanup;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.ProjectCommands;

[Verb("cleanup", false, null, HelpText = "Cleans up old backup and result files based on a configurable retention policy.")]
public class ProjectCleanupArgs
{
	public const string Verb = "cleanup";

	[Value(0, MetaName = "project", Required = true, HelpText = "Path to the project file (*.ras) or a directory containing one.")]
	public string ProjectFilename { get; set; }

	[Option('f', "force", Required = false, HelpText = "Skip per-file prompts, delete all candidates.")]
	public bool Force { get; set; }

	[Option("dry-run", Required = false, HelpText = "Show what would be deleted without actually deleting.")]
	public bool DryRun { get; set; }

	[Option("set-policy", Required = false, HelpText = "Save the policy to the project file without executing cleanup. Combine with --keep-all-days, --hourly-days, etc.")]
	public bool SetPolicy { get; set; }

	[Option("keep-all-days", Required = false, Default = -1, HelpText = "Days to keep all backups (default: 7).")]
	public int KeepAllDays { get; set; } = -1;

	[Option("hourly-days", Required = false, Default = -1, HelpText = "Days for hourly retention window (default: 30).")]
	public int HourlyDays { get; set; } = -1;

	[Option("daily-days", Required = false, Default = -1, HelpText = "Days for daily retention window (default: 365).")]
	public int DailyDays { get; set; } = -1;

	[Option("verbose", Required = false, HelpText = "Show detailed evaluation of every file explaining why it would or would not be cleaned.")]
	public bool Verbose { get; set; }

	[Option("skip-auxiliary", Required = false, HelpText = "Don't clean result auxiliary files (export directories).")]
	public bool SkipAuxiliary { get; set; }

	private bool HasPolicyOverrides
	{
		get
		{
			if (KeepAllDays < 0 && HourlyDays < 0 && DailyDays < 0)
			{
				return SkipAuxiliary;
			}
			return true;
		}
	}

	private CleanupPolicy BuildPolicy(Project project, out string policySource)
	{
		bool hasCleanupPolicy = project.CleanupPolicy != null;
		CleanupPolicy cleanupPolicy = project.CleanupPolicy ?? CleanupPolicyDefaults.Geometry.Default;
		if (HasPolicyOverrides)
		{
			policySource = (hasCleanupPolicy ? "Project policy (with CLI overrides)" : "Default policy (with CLI overrides)");
		}
		else
		{
			policySource = (hasCleanupPolicy ? "Project policy" : "Default policy (no policy saved to project)");
		}
		if (KeepAllDays >= 0)
		{
			cleanupPolicy.KeepAll = TimeSpan.FromDays(KeepAllDays);
		}
		if (HourlyDays >= 0)
		{
			cleanupPolicy.KeepHourly = TimeSpan.FromDays(HourlyDays);
		}
		if (DailyDays >= 0)
		{
			cleanupPolicy.KeepDaily = TimeSpan.FromDays(DailyDays);
		}
		return cleanupPolicy;
	}

	private bool GetCleanAuxiliary(Project project)
	{
		if (SkipAuxiliary)
		{
			return false;
		}
		return project.CleanResultAuxiliaryFiles;
	}

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		OperationResult operationResult = Project.TryLoad(ProjectFilename, out var prj);
		if (!operationResult)
		{
			progressReporter.ReportMessage(operationResult.GetConcatenatedMessages());
			return false;
		}
		CleanupPolicy policy = BuildPolicy(prj, out var policySource);
		bool cleanAuxiliary = GetCleanAuxiliary(prj);
		PrintPolicy(policy, cleanAuxiliary, policySource, progressReporter);
		if (SetPolicy)
		{
			return ExecuteSetPolicy(prj, policy, cleanAuxiliary, progressReporter);
		}
		progressReporter.ReportMessage("Scanning project: " + prj.Name);
		CleanupReport cleanupReport = prj.ScanForCleanup(policy, cleanAuxiliary, Verbose);
		cleanupReport.WriteSummary(progressReporter);
		if (Verbose)
		{
			PrintVerboseReport(cleanupReport.Evaluations, progressReporter);
		}
		if (cleanupReport.Candidates.Count == 0)
		{
			progressReporter.ReportMessage("Nothing to clean up.");
			return true;
		}
		if (DryRun)
		{
			PrintCandidateList(cleanupReport.Candidates, progressReporter);
			progressReporter.ReportMessage("Dry run - no files were deleted.");
			return true;
		}
		if (Force)
		{
			CleanupResult cleanupResult = prj.ExecuteCleanup(cleanupReport, progressReporter);
			cleanupResult.WriteSummary(progressReporter);
			return cleanupResult.FilesFailed == 0;
		}
		return ExecuteInteractive(cleanupReport, progressReporter);
	}

	private static bool ExecuteSetPolicy(Project project, CleanupPolicy policy, bool cleanAuxiliary, ProgressReporter pr)
	{
		project.CleanupPolicy = policy;
		project.CleanResultAuxiliaryFiles = cleanAuxiliary;
		project.Save();
		pr.ReportMessage("Cleanup policy saved to project: " + project.Name);
		pr.ReportMessage($"  Keep all:         {policy.KeepAll.TotalDays} days");
		pr.ReportMessage($"  Hourly retention: {policy.KeepHourly.TotalDays} days");
		pr.ReportMessage($"  Daily retention:  {policy.KeepDaily.TotalDays} days");
		pr.ReportMessage($"  Clean auxiliary:  {cleanAuxiliary}");
		return true;
	}

	private static bool ExecuteInteractive(CleanupReport report, ProgressReporter pr)
	{
		List<CleanupCandidate> cleanupCandidates = new List<CleanupCandidate>();
		foreach (CleanupCandidate candidate in report.Candidates)
		{
			Console.Write($"  Delete {candidate.Category} [{candidate.ParentName}] {Path.GetFileName(candidate.FilePath)} ({candidate.PrettySize}, {candidate.Reason})? [y/N] ");
			string readLineConsoleTerm = Console.ReadLine()?.Trim().ToLowerInvariant();
			if (readLineConsoleTerm == "y" || readLineConsoleTerm == "yes")
			{
				cleanupCandidates.Add(candidate);
			}
		}
		if (cleanupCandidates.Count == 0)
		{
			pr.ReportMessage("No files approved for deletion.");
			return true;
		}
		CleanupResult cleanupResult = CleanupExecutor.ExecuteSubset(cleanupCandidates, pr);
		cleanupResult.WriteSummary(pr);
		return cleanupResult.FilesFailed == 0;
	}

	private static void PrintPolicy(CleanupPolicy policy, bool cleanAuxiliary, string source, ProgressReporter pr)
	{
		pr.ReportMessage("Policy: " + source);
		pr.ReportMessage($"  Keep all:         {policy.KeepAll.TotalDays} days");
		pr.ReportMessage($"  Hourly retention: {policy.KeepHourly.TotalDays} days");
		pr.ReportMessage($"  Daily retention:  {policy.KeepDaily.TotalDays} days");
		pr.ReportMessage($"  Weekly retention: {policy.KeepWeekly.TotalDays} days");
		pr.ReportMessage($"  Clean auxiliary:  {cleanAuxiliary}");
	}

	private static void PrintCandidateList(List<CleanupCandidate> candidates, ProgressReporter pr)
	{
		pr.ReportMessage("");
		pr.ReportMessage("Candidates for deletion:");
		foreach (CleanupCandidate candidate in candidates)
		{
			pr.ReportMessage($"  [{candidate.Category}] {candidate.ParentName}: {Path.GetFileName(candidate.FilePath)} ({candidate.PrettySize}) - {candidate.Reason}");
			foreach (string auxiliaryFile in candidate.AuxiliaryFiles)
			{
				pr.ReportMessage("    + " + auxiliaryFile);
			}
		}
	}

	private static void PrintVerboseReport(List<CleanupEvaluation> evaluations, ProgressReporter pr)
	{
		if (evaluations.Count == 0)
		{
			return;
		}
		pr.ReportMessage("");
		pr.ReportMessage("Detailed file evaluation:");
		foreach (IGrouping<(string, string), CleanupEvaluation> iGrouping in from e in evaluations
			group e by (Category: e.Category, ParentName: e.ParentName) into g
			orderby g.Key.Category, g.Key.ParentName
			select g)
		{
			pr.ReportMessage($"  [{iGrouping.Key.Item1}] {iGrouping.Key.Item2}:");
			foreach (CleanupEvaluation orderByDescending in iGrouping.OrderByDescending((CleanupEvaluation e) => e.Timestamp))
			{
				string retainedOrderByDescendingTerm = (orderByDescending.Retained ? "KEEP" : "DEL ");
				string prettyByteCount = FileHelpers.PrettyByteCount(orderByDescending.SizeBytes);
				pr.ReportMessage($"    {retainedOrderByDescendingTerm}  {Path.GetFileName(orderByDescending.FilePath)} ({prettyByteCount}) - {orderByDescending.Reason}");
			}
		}
	}
}
