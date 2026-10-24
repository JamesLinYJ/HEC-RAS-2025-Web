using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.ProjectCommands;

internal static class ProjectListPlanDetailedHelper
{
	private sealed class PlanEntry
	{
		public int Index { get; set; }

		public string Name { get; set; }

		public string ShortID { get; set; }

		public string Filename { get; set; }

		public string GeometryName { get; set; }

		public string BoundaryConditionName { get; set; }

		public string Solver { get; set; }

		public DateTime StartTime { get; set; }

		public DateTime EndTime { get; set; }

		public TimeSpan ComputeTimestep { get; set; }

		public TimeSpan BaseOutputInterval { get; set; }
	}

	public static bool Run(string projectPath, OutputFormat outputFormat)
	{
		string currentDirectory = (string.IsNullOrWhiteSpace(projectPath) ? Directory.GetCurrentDirectory() : projectPath);
		OperationResult operationResult = Project.TryLoad(currentDirectory, out var prj);
		if (!operationResult)
		{
			Console.WriteLine("Could not load project from " + currentDirectory.InDoubleQuotes() + ": " + operationResult.GetConcatenatedMessages());
			return false;
		}
		List<PlanEntry> entries = BuildEntries(prj);
		Console.WriteLine(outputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(entries, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(prj, entries), 
			_ => RenderText(prj, entries), 
		});
		return true;
	}

	private static List<PlanEntry> BuildEntries(Project project)
	{
		List<PlanEntry> planEntries = new List<PlanEntry>(project.Plans.Count);
		for (int i = 0; i < project.Plans.Count; i++)
		{
			Plan plan = project.Plans[i];
			PlanEntry planEntry = new PlanEntry
			{
				Index = i,
				Name = plan.Name,
				ShortID = plan.ShortID,
				Filename = plan.Filename,
				Solver = plan.SolverType.ToString(),
				ComputeTimestep = plan.ComputeTimestep,
				BaseOutputInterval = plan.BaseOutputInterval,
				StartTime = plan.StartTime,
				EndTime = plan.EndTime
			};
			PlanAssociation planAssociation = project.GetPlanAssociation(plan);
			if (planAssociation != null)
			{
				planEntry.GeometryName = (string.IsNullOrEmpty(planAssociation.GeometryName) ? null : planAssociation.GeometryName);
				planEntry.BoundaryConditionName = (string.IsNullOrEmpty(planAssociation.BoundaryConditionName) ? null : planAssociation.BoundaryConditionName);
			}
			planEntries.Add(planEntry);
		}
		return planEntries;
	}

	private static string RenderText(Project project, List<PlanEntry> entries)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("Project: ");
		handler.AppendFormatted(project.Filename);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("Plans:   ");
		handler.AppendFormatted(entries.Count);
		stringBuilder4.AppendLine(ref handler);
		foreach (PlanEntry entry in entries)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder2);
			handler.AppendLiteral("  [");
			handler.AppendFormatted(entry.Index);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(entry.Name);
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("      File:     ");
			handler.AppendFormatted(entry.Filename);
			stringBuilder6.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("      Short ID: ");
			handler.AppendFormatted(string.IsNullOrEmpty(entry.ShortID) ? "(none)" : entry.ShortID);
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("      Geometry: ");
			handler.AppendFormatted(entry.GeometryName ?? "(unassociated)");
			stringBuilder8.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("      BC:       ");
			handler.AppendFormatted(entry.BoundaryConditionName ?? "(unassociated)");
			stringBuilder9.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("      Solver:   ");
			handler.AppendFormatted(entry.Solver);
			stringBuilder10.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 2, stringBuilder2);
			handler.AppendLiteral("      Window:   ");
			handler.AppendFormatted(entry.StartTime, "yyyy-MM-dd HH:mm:ss");
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(entry.EndTime, "yyyy-MM-dd HH:mm:ss");
			stringBuilder11.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 2, stringBuilder2);
			handler.AppendLiteral("      Steps:    compute=");
			handler.AppendFormatted(entry.ComputeTimestep);
			handler.AppendLiteral(", output=");
			handler.AppendFormatted(entry.BaseOutputInterval);
			stringBuilder12.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderMarkdown(Project project, List<PlanEntry> entries)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# Plans in " + (project.Name ?? "(project)"));
		stringBuilder.AppendLine();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("Project file: `");
		handler.AppendFormatted(project.Filename);
		handler.AppendLiteral("`");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("## Plans (");
		handler.AppendFormatted(entries.Count);
		handler.AppendLiteral(")");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (entries.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
			return stringBuilder.ToString().TrimEnd();
		}
		stringBuilder.AppendLine("| # | Name | Geometry | Boundary Condition | Solver | Start | End |");
		stringBuilder.AppendLine("|---|------|----------|--------------------|--------|-------|-----|");
		foreach (PlanEntry entry in entries)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(22, 7, stringBuilder2);
			handler.AppendLiteral("| ");
			handler.AppendFormatted(entry.Index);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.Name);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.GeometryName ?? "(unassociated)");
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.BoundaryConditionName ?? "(unassociated)");
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.Solver);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.StartTime, "yyyy-MM-dd HH:mm:ss");
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.EndTime, "yyyy-MM-dd HH:mm:ss");
			handler.AppendLiteral(" |");
			stringBuilder5.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}
}
