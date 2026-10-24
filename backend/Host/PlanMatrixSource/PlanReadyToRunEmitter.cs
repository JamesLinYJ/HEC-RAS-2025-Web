// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ras.Layers;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public static class PlanReadyToRunEmitter
{
	public static PlanEmitSummary EmitAll(Plan plan, string directory, IReadOnlyList<PlanSweepCombination> combinations, PlanEmitOptions options, Action<PlanSweepCombination> onStarted, Action<PlanSweepCombination, PlanEmitOutcome> onCompleted, ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		PlanEmitSummary planEmitSummary = new PlanEmitSummary();
		Dictionary<string, object> originals = Snapshot(plan, combinations);
		Dictionary<PlanSweepCombination, PlanEmitOutcome> outcomes = new Dictionary<PlanSweepCombination, PlanEmitOutcome>();
		List<PlanSweepCombination> planSweepCombinations = combinations.Where((PlanSweepCombination c) => !c.IsExcluded).ToList();
		planEmitSummary.Excluded = combinations.Count - planSweepCombinations.Count;
		Directory.CreateDirectory(directory);
		try
		{
			for (int intervalIndex = 0; intervalIndex < planSweepCombinations.Count; intervalIndex++)
			{
				PlanSweepCombination planSweepCombination = planSweepCombinations[intervalIndex];
				if (reporter.CancelRequested)
				{
					planEmitSummary.WasCancelled = true;
					Report(planSweepCombination, new PlanEmitOutcome
					{
						Status = PlanEmitStatus.Cancelled,
						Message = "Cancelled"
					});
					continue;
				}
				reporter.ReportMessage($"[{intervalIndex + 1}/{planSweepCombinations.Count}] {planSweepCombination.Filename}");
				onStarted?.Invoke(planSweepCombination);
				PlanEmitOutcome outcome = EmitOne(plan, directory, planSweepCombination, originals, options, reporter.SubTask(planSweepCombination.Filename, (float)intervalIndex / (float)planSweepCombinations.Count, 1f / (float)planSweepCombinations.Count), planEmitSummary);
				Report(planSweepCombination, outcome);
				if (planEmitSummary.WasAborted)
				{
					reporter.ReportMessage("Stopping the matrix run: " + planEmitSummary.AbortReason);
					break;
				}
			}
		}
		finally
		{
			Restore(plan, originals);
		}
		if (options.WriteManifest)
		{
			planEmitSummary.ManifestPath = TryWriteManifest(plan, directory, combinations, outcomes, reporter);
		}
		return planEmitSummary;
		void Report(PlanSweepCombination combo, PlanEmitOutcome planEmitOutcome)
		{
			outcomes[combo] = planEmitOutcome;
			onCompleted?.Invoke(combo, planEmitOutcome);
		}
	}

	private static PlanEmitOutcome EmitOne(Plan plan, string directory, PlanSweepCombination combo, Dictionary<string, object> originals, PlanEmitOptions options, ProgressReporter reporter, PlanEmitSummary summary)
	{
		string path = Path.Combine(directory, combo.Filename);
		try
		{
			if (File.Exists(path))
			{
				if (!options.Overwrite)
				{
					summary.Skipped++;
					return new PlanEmitOutcome
					{
						Status = PlanEmitStatus.Skipped,
						FilePath = path,
						Message = "File already exists"
					};
				}
				File.Delete(path);
			}
			Apply(plan, combo, originals);
			OperationResult operationResult = plan.TryPrepareCompute(path, out var readyToRun, reporter);
			using (readyToRun)
			{
				if (!operationResult.Result)
				{
					summary.Failed++;
					return new PlanEmitOutcome
					{
						Status = PlanEmitStatus.Failed,
						FilePath = path,
						Message = operationResult.GetConcatenatedMessages(" | ")
					};
				}
				if (options.NameEmbeddedPlan)
				{
					readyToRun?.RenameEmbeddedPlan(EmbeddedPlanName(combo));
				}
			}
			summary.Written++;
			return new PlanEmitOutcome
			{
				Status = PlanEmitStatus.Written,
				FilePath = path,
				FileSizeBytes = new FileInfo(path).Length
			};
		}
		catch (Exception ex)
		{
			summary.Failed++;
			summary.WasAborted = true;
			summary.AbortReason = ex.Message;
			return new PlanEmitOutcome
			{
				Status = PlanEmitStatus.Failed,
				FilePath = path,
				Message = ex.Message
			};
		}
	}

	private static string EmbeddedPlanName(PlanSweepCombination combo)
	{
		string filename = combo.Filename;
		if (filename.EndsWith(".r2r.h5", StringComparison.OrdinalIgnoreCase))
		{
			filename = filename.Substring(0, filename.Length - ".r2r.h5".Length);
		}
		return filename;
	}

	private static void Apply(Plan plan, PlanSweepCombination combo, Dictionary<string, object> originals)
	{
		foreach (KeyValuePair<string, object> original in originals)
		{
			PlanSweepProperty.Find(original.Key).SetValue(plan, original.Value);
		}
		foreach (PlanSweepAssignment assignment in combo.Assignments)
		{
			if (assignment.IsRelevant)
			{
				assignment.Property.SetValue(plan, assignment.Value);
			}
		}
	}

	private static Dictionary<string, object> Snapshot(Plan plan, IReadOnlyList<PlanSweepCombination> combinations)
	{
		Dictionary<string, object> ordinal = new Dictionary<string, object>(StringComparer.Ordinal);
		foreach (PlanSweepCombination combination in combinations)
		{
			foreach (PlanSweepAssignment assignment in combination.Assignments)
			{
				string propertyName = assignment.Property.PropertyName;
				if (!ordinal.ContainsKey(propertyName))
				{
					ordinal[propertyName] = assignment.Property.GetValue(plan);
				}
			}
		}
		return ordinal;
	}

	private static void Restore(Plan plan, Dictionary<string, object> originals)
	{
		foreach (KeyValuePair<string, object> original in originals)
		{
			PlanSweepProperty.Find(original.Key).SetValue(plan, original.Value);
		}
	}

	private static string TryWriteManifest(Plan plan, string directory, IReadOnlyList<PlanSweepCombination> combinations, IReadOnlyDictionary<PlanSweepCombination, PlanEmitOutcome> outcomes, ProgressReporter reporter)
	{
		try
		{
			List<PlanSweepProperty> distinct = (from a in combinations.SelectMany((PlanSweepCombination c) => c.Assignments)
				select a.Property).Distinct().ToList();
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("File,Status,Note");
			foreach (PlanSweepProperty distinct2 in distinct)
			{
				stringBuilder.Append(',');
				stringBuilder.Append(Escape(distinct2.DisplayName));
			}
			stringBuilder.AppendLine();
			foreach (PlanSweepCombination combination in combinations)
			{
				outcomes.TryGetValue(combination, out var localValue);
				PlanEmitStatus planEmitStatus = (combination.IsExcluded ? PlanEmitStatus.Excluded : localValue.Status);
				string exclusionReason = (combination.IsExcluded ? combination.ExclusionReason : localValue.Message);
				stringBuilder.Append(Escape(combination.Filename));
				stringBuilder.Append(',');
				stringBuilder.Append(planEmitStatus);
				stringBuilder.Append(',');
				stringBuilder.Append(Escape(exclusionReason));
				foreach (PlanSweepProperty column in distinct)
				{
					stringBuilder.Append(',');
					PlanSweepAssignment planSweepAssignment = combination.Assignments.FirstOrDefault((PlanSweepAssignment a) => a.Property == column);
					if (planSweepAssignment.Property != null)
					{
						string describe = planSweepAssignment.Property.Describe(planSweepAssignment.Value);
						if (!planSweepAssignment.IsRelevant)
						{
							describe += " (ignored)";
						}
						stringBuilder.Append(Escape(describe));
					}
				}
				stringBuilder.AppendLine();
			}
			string path = Path.Combine(directory, PlanSweepProperty.Sanitize(plan.Name) + "_matrix.csv");
			File.WriteAllText(path, stringBuilder.ToString());
			return path;
		}
		catch (Exception ex)
		{
			reporter.ReportMessage("Could not write the matrix manifest: " + ex.Message);
			return null;
		}
	}

	private static string Escape(string textValue)
	{
		if (string.IsNullOrEmpty(textValue))
		{
			return string.Empty;
		}
		if (!textValue.Contains(',') && !textValue.Contains('"') && !textValue.Contains('\n'))
		{
			return textValue;
		}
		return "\"" + textValue.Replace("\"", "\"\"") + "\"";
	}
}
