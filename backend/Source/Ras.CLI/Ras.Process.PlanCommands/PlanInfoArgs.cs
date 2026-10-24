using System;
using System.Text;
using System.Text.Json;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;

namespace Ras.Process.PlanCommands;

[Verb("info", false, null, HelpText = "Prints information about an existing RAS plan file (*.h5).")]
public class PlanInfoArgs
{
	internal sealed class PlanInfoModel
	{
		public string Filename { get; set; }

		public string FileType { get; set; }

		public string FileVersion { get; set; }

		public string Name { get; set; }

		public string ShortID { get; set; }

		public string ProjectFilename { get; set; }

		public string GeometryName { get; set; }

		public string BoundaryConditionName { get; set; }

		public bool IsResult { get; set; }

		public string SolverType { get; set; }

		public string EquationSet { get; set; }

		public string TimeStepping { get; set; }

		public bool UseGPU { get; set; }

		public bool UseFBF { get; set; }

		public bool UseAdvection { get; set; }

		public bool UseDiffusion { get; set; }

		public int CoreCount { get; set; }

		public string TimeWindowMode { get; set; }

		public DateTime StartTime { get; set; }

		public DateTime EndTime { get; set; }

		public TimeSpan ComputeTimestep { get; set; }

		public TimeSpan BaseOutputInterval { get; set; }

		public int ExpectedProfileCount { get; set; }
	}

	public const string Verb = "info";

	[Value(0, MetaName = "plan", Required = true, HelpText = "Plan file (*.h5).")]
	public string PlanFilename { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'name'). Ignores --format.")]
	public string GetPath { get; set; }

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (!TryResolvePlan(out var pl))
		{
			return false;
		}
		PlanInfoModel planInfoModel = BuildModel(pl);
		if (!string.IsNullOrEmpty(GetPath))
		{
			return OutputFormats.WriteGet(planInfoModel, GetPath);
		}
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(planInfoModel, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(planInfoModel), 
			OutputFormat.Properties => OutputFormats.Properties(planInfoModel), 
			_ => RenderText(planInfoModel), 
		});
		return true;
	}

	private bool TryResolvePlan(out Plan pl)
	{
		return CliHelpers.TryResolveCoreFile(PlanFilename, CoreFileType.Plan, (string fn) => new Plan(fn), out pl);
	}

	internal static PlanInfoModel BuildModel(Plan plan)
	{
		PlanInfoModel planInfoModel = new PlanInfoModel
		{
			Filename = plan.Filename,
			FileType = "RAS 2025 Plan",
			FileVersion = plan.GetCurrentFileFormatVersion(),
			Name = plan.Name,
			ShortID = plan.ShortID,
			SolverType = plan.SolverType.ToString(),
			EquationSet = plan.EquationSet.ToString(),
			TimeStepping = plan.TimeStepping.ToString(),
			UseGPU = plan.UseGPU,
			UseFBF = plan.UseFBF,
			UseAdvection = plan.UseAdvection,
			UseDiffusion = plan.UseDiffusion,
			CoreCount = plan.CoreCount,
			TimeWindowMode = plan.TimeWindowMode.ToString(),
			StartTime = plan.StartTime,
			EndTime = plan.EndTime,
			ComputeTimestep = plan.ComputeTimestep,
			BaseOutputInterval = plan.BaseOutputInterval,
			ExpectedProfileCount = plan.ExpectedProfileCount,
			IsResult = plan.IsResult
		};
		if (plan.Project != null)
		{
			planInfoModel.ProjectFilename = plan.Project.Filename;
			if (!plan.IsResult)
			{
				PlanAssociation planAssociation = plan.Project.GetPlanAssociation(plan);
				if (planAssociation != null)
				{
					planInfoModel.GeometryName = (string.IsNullOrEmpty(planAssociation.GeometryName) ? null : planAssociation.GeometryName);
					planInfoModel.BoundaryConditionName = (string.IsNullOrEmpty(planAssociation.BoundaryConditionName) ? null : planAssociation.BoundaryConditionName);
				}
			}
		}
		return planInfoModel;
	}

	internal static string RenderText(PlanInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename:        " + m.Filename);
		stringBuilder.AppendLine("File Type:       " + m.FileType);
		stringBuilder.AppendLine("File Version:    " + m.FileVersion);
		stringBuilder.AppendLine("Name:            " + (m.Name ?? "(unknown)"));
		stringBuilder.AppendLine("Short ID:        " + (string.IsNullOrEmpty(m.ShortID) ? "(none)" : m.ShortID));
		if (m.IsResult)
		{
			stringBuilder.AppendLine("Embedded in:     Result file");
		}
		stringBuilder.AppendLine("Project:         " + (m.ProjectFilename ?? "(none)"));
		stringBuilder.AppendLine("Geometry:        " + (m.GeometryName ?? "(unassociated)"));
		stringBuilder.AppendLine("Boundary Cond.:  " + (m.BoundaryConditionName ?? "(unassociated)"));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Solver:          " + m.SolverType + (m.UseGPU ? " (GPU)" : ""));
		stringBuilder.AppendLine("Equation Set:    " + m.EquationSet);
		stringBuilder.AppendLine("Time Stepping:   " + m.TimeStepping);
		stringBuilder.AppendLine("Use FBF:         " + m.UseFBF);
		stringBuilder.AppendLine("Use Advection:   " + m.UseAdvection);
		stringBuilder.AppendLine("Use Diffusion:   " + m.UseDiffusion);
		stringBuilder.AppendLine("Core Count:      " + m.CoreCount);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Time Window:     " + m.TimeWindowMode);
		stringBuilder.AppendLine("Start:           " + m.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("End:             " + m.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("Compute Step:    " + m.ComputeTimestep);
		stringBuilder.AppendLine("Output Interval: " + m.BaseOutputInterval);
		stringBuilder.AppendLine("Profiles:        " + m.ExpectedProfileCount);
		return stringBuilder.ToString().TrimEnd();
	}

	internal static string RenderMarkdown(PlanInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# Plan: " + (m.Name ?? "(unknown)"));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Filename | `");
		handler.AppendFormatted(m.Filename);
		handler.AppendLiteral("` |");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("| File Type | ");
		handler.AppendFormatted(m.FileType);
		handler.AppendLiteral(" |");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler.AppendLiteral("| File Version | ");
		handler.AppendFormatted(m.FileVersion);
		handler.AppendLiteral(" |");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("| Name | ");
		handler.AppendFormatted(m.Name ?? "(unknown)");
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Short ID | ");
		handler.AppendFormatted(string.IsNullOrEmpty(m.ShortID) ? "(none)" : m.ShortID);
		handler.AppendLiteral(" |");
		stringBuilder7.AppendLine(ref handler);
		if (m.IsResult)
		{
			stringBuilder.AppendLine("| Embedded in | Result file |");
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("| Project | ");
		handler.AppendFormatted((m.ProjectFilename != null) ? ("`" + m.ProjectFilename + "`") : "(none)");
		handler.AppendLiteral(" |");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Geometry | ");
		handler.AppendFormatted(m.GeometryName ?? "(unassociated)");
		handler.AppendLiteral(" |");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
		handler.AppendLiteral("| Boundary Condition | ");
		handler.AppendFormatted(m.BoundaryConditionName ?? "(unassociated)");
		handler.AppendLiteral(" |");
		stringBuilder10.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## Solver");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 2, stringBuilder2);
		handler.AppendLiteral("| Solver | ");
		handler.AppendFormatted(m.SolverType);
		handler.AppendFormatted(m.UseGPU ? " (GPU)" : "");
		handler.AppendLiteral(" |");
		stringBuilder11.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler.AppendLiteral("| Equation Set | ");
		handler.AppendFormatted(m.EquationSet);
		handler.AppendLiteral(" |");
		stringBuilder12.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder13 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("| Time Stepping | ");
		handler.AppendFormatted(m.TimeStepping);
		handler.AppendLiteral(" |");
		stringBuilder13.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder14 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("| Use FBF | ");
		handler.AppendFormatted(m.UseFBF);
		handler.AppendLiteral(" |");
		stringBuilder14.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder15 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("| Use Advection | ");
		handler.AppendFormatted(m.UseAdvection);
		handler.AppendLiteral(" |");
		stringBuilder15.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder16 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("| Use Diffusion | ");
		handler.AppendFormatted(m.UseDiffusion);
		handler.AppendLiteral(" |");
		stringBuilder16.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder17 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Core Count | ");
		handler.AppendFormatted(m.CoreCount);
		handler.AppendLiteral(" |");
		stringBuilder17.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## Time Window");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder18 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("| Mode | ");
		handler.AppendFormatted(m.TimeWindowMode);
		handler.AppendLiteral(" |");
		stringBuilder18.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder19 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("| Start | ");
		handler.AppendFormatted(m.StartTime, "yyyy-MM-dd HH:mm:ss");
		handler.AppendLiteral(" |");
		stringBuilder19.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder20 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("| End | ");
		handler.AppendFormatted(m.EndTime, "yyyy-MM-dd HH:mm:ss");
		handler.AppendLiteral(" |");
		stringBuilder20.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder21 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler.AppendLiteral("| Compute Step | ");
		handler.AppendFormatted(m.ComputeTimestep);
		handler.AppendLiteral(" |");
		stringBuilder21.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder22 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
		handler.AppendLiteral("| Output Interval | ");
		handler.AppendFormatted(m.BaseOutputInterval);
		handler.AppendLiteral(" |");
		stringBuilder22.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder23 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Profiles | ");
		handler.AppendFormatted(m.ExpectedProfileCount);
		handler.AppendLiteral(" |");
		stringBuilder23.AppendLine(ref handler);
		return stringBuilder.ToString().TrimEnd();
	}
}
