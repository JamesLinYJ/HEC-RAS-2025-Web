using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Layers.Results;
using Ras.Output;
using Ras.Process.BoundaryConditionCommands;
using Ras.Process.GeometryCommands;
using Ras.Process.Helpers;
using Ras.Process.PlanCommands;

namespace Ras.Process.ResultCommands;

[Verb("info", false, null, HelpText = "Prints information about an existing RAS 2025+ result file (*.h5).")]
public class ResultInfoArgs
{
	private sealed class ResultInfoModel
	{
		public string Filename { get; set; }

		public string FileType { get; set; }

		public string Name { get; set; }

		public string ShortID { get; set; }

		public string Solver { get; set; }

		public string SoftwareVersion { get; set; }

		public bool ComputedSuccessfully { get; set; }

		public string FailureReason { get; set; }

		public DateTime ComputeStartTime { get; set; }

		public TimeSpan TotalDuration { get; set; }

		public int ProfileCount { get; set; }

		public DateTime? StartTime { get; set; }

		public DateTime? EndTime { get; set; }

		public List<OutputBlockModel> OutputBlocks { get; set; } = new List<OutputBlockModel>();

		public PlanInfoArgs.PlanInfoModel Plan { get; set; }

		public GeometryInfoArgs.GeometryInfoModel Geometry { get; set; }

		public BoundaryConditionInfoArgs.BoundaryConditionInfoModel BoundaryCondition { get; set; }
	}

	private sealed class OutputBlockModel
	{
		public string Name { get; set; }

		public int ProfileCount { get; set; }
	}

	public const string Verb = "info";

	[Value(0, MetaName = "result", Required = true, HelpText = "Result file (*.h5).")]
	public string ResultFilename { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'name' or 'outputBlocks[0].name'). Ignores --format.")]
	public string GetPath { get; set; }

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (!TryResolveResult(out var result))
		{
			return false;
		}
		using (result)
		{
			ResultInfoModel resultInfoModel = BuildModel(result);
			if (!string.IsNullOrEmpty(GetPath))
			{
				return OutputFormats.WriteGet(resultInfoModel, GetPath);
			}
			Console.WriteLine(OutputFormat switch
			{
				OutputFormat.Json => JsonSerializer.Serialize(resultInfoModel, OutputFormats.Json), 
				OutputFormat.Markdown => RenderMarkdown(resultInfoModel), 
				OutputFormat.Properties => OutputFormats.Properties(resultInfoModel), 
				_ => RenderText(resultInfoModel), 
			});
		}
		return true;
	}

	private bool TryResolveResult(out Result result)
	{
		return CliHelpers.TryResolveCoreFile(ResultFilename, CoreFileType.Result, (string fn) => new Result(fn), out result);
	}

	private static ResultInfoModel BuildModel(Result result)
	{
		ResultInfoModel resultInfoModel = new ResultInfoModel
		{
			Filename = result.Filename,
			FileType = "RAS 2025 Result",
			Name = result.Name,
			ShortID = result.Plan?.ShortID,
			ProfileCount = result.ProfileCount
		};
		ComputeSummary computeSummary = result.ComputeSummary;
		if (computeSummary != null)
		{
			resultInfoModel.Solver = computeSummary.Solver;
			resultInfoModel.SoftwareVersion = computeSummary.SoftwareVersion;
			resultInfoModel.ComputedSuccessfully = computeSummary.Success;
			resultInfoModel.FailureReason = (string.IsNullOrEmpty(computeSummary.FailureReason) ? null : computeSummary.FailureReason);
			resultInfoModel.ComputeStartTime = computeSummary.StartTime;
			resultInfoModel.TotalDuration = computeSummary.TotalDuration;
		}
		IHorizontalOutputBlock baseOutputBlock = result.BaseOutputBlock;
		if (baseOutputBlock != null)
		{
			IReadOnlyList<DateTime> times = baseOutputBlock.Times;
			if (times != null && times.Count > 0)
			{
				resultInfoModel.StartTime = times[0];
				resultInfoModel.EndTime = times[times.Count - 1];
			}
		}
		foreach (IHorizontalOutputBlock outputBlock in result.OutputBlocks)
		{
			resultInfoModel.OutputBlocks.Add(new OutputBlockModel
			{
				Name = outputBlock.Name,
				ProfileCount = outputBlock.ProfileCount
			});
		}
		if (result.Plan != null)
		{
			resultInfoModel.Plan = PlanInfoArgs.BuildModel(result.Plan);
		}
		if (result.Geometry != null)
		{
			resultInfoModel.Geometry = GeometryInfoArgs.BuildModel(result.Geometry);
		}
		if (result.BoundaryConditions != null)
		{
			resultInfoModel.BoundaryCondition = BoundaryConditionInfoArgs.BuildModel(result.BoundaryConditions);
		}
		return resultInfoModel;
	}

	private static string RenderText(ResultInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename:        " + m.Filename);
		stringBuilder.AppendLine("File Type:       " + m.FileType);
		stringBuilder.AppendLine("Name:            " + (m.Name ?? "(unknown)"));
		stringBuilder.AppendLine("Short ID:        " + (string.IsNullOrEmpty(m.ShortID) ? "(none)" : m.ShortID));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Solver:          " + (m.Solver ?? "(unknown)"));
		stringBuilder.AppendLine("Software:        " + (m.SoftwareVersion ?? "(unknown)"));
		stringBuilder.AppendLine("Computed OK:     " + m.ComputedSuccessfully);
		if (m.FailureReason != null)
		{
			stringBuilder.AppendLine("Failure Reason:  " + m.FailureReason);
		}
		stringBuilder.AppendLine("Compute Start:   " + m.ComputeStartTime.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("Compute Time:    " + m.TotalDuration);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Profiles:        " + m.ProfileCount);
		if (m.StartTime.HasValue)
		{
			stringBuilder.AppendLine("Sim Start:       " + m.StartTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
		}
		if (m.EndTime.HasValue)
		{
			stringBuilder.AppendLine("Sim End:         " + m.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
		}
		stringBuilder.AppendLine("Output Blocks:   " + m.OutputBlocks.Count);
		foreach (OutputBlockModel outputBlock in m.OutputBlocks)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(outputBlock.Name);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(outputBlock.ProfileCount);
			handler.AppendLiteral(" profiles)");
			stringBuilder2.AppendLine(ref handler);
		}
		AppendTextSection(stringBuilder, "Plan", (m.Plan != null) ? PlanInfoArgs.RenderText(m.Plan) : null);
		AppendTextSection(stringBuilder, "Geometry", (m.Geometry != null) ? GeometryInfoArgs.RenderText(m.Geometry) : null);
		AppendTextSection(stringBuilder, "Boundary Condition", (m.BoundaryCondition != null) ? BoundaryConditionInfoArgs.RenderText(m.BoundaryCondition) : null);
		return stringBuilder.ToString().TrimEnd();
	}

	private static void AppendTextSection(StringBuilder sb, string title, string body)
	{
		if (body != null)
		{
			sb.AppendLine();
			sb.AppendLine("=== " + title + " ===");
			sb.AppendLine(body);
		}
	}

	private static string RenderMarkdown(ResultInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# Result: " + (m.Name ?? "(unknown)"));
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
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("| Name | ");
		handler.AppendFormatted(m.Name ?? "(unknown)");
		handler.AppendLiteral(" |");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Short ID | ");
		handler.AppendFormatted(string.IsNullOrEmpty(m.ShortID) ? "(none)" : m.ShortID);
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## Compute");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("| Solver | ");
		handler.AppendFormatted(m.Solver ?? "(unknown)");
		handler.AppendLiteral(" |");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Software | ");
		handler.AppendFormatted(m.SoftwareVersion ?? "(unknown)");
		handler.AppendLiteral(" |");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
		handler.AppendLiteral("| Computed OK | ");
		handler.AppendFormatted(m.ComputedSuccessfully);
		handler.AppendLiteral(" |");
		stringBuilder9.AppendLine(ref handler);
		if (m.FailureReason != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 1, stringBuilder2);
			handler.AppendLiteral("| Failure Reason | ");
			handler.AppendFormatted(m.FailureReason);
			handler.AppendLiteral(" |");
			stringBuilder10.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("| Compute Start | ");
		handler.AppendFormatted(m.ComputeStartTime, "yyyy-MM-dd HH:mm:ss");
		handler.AppendLiteral(" |");
		stringBuilder11.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler.AppendLiteral("| Compute Time | ");
		handler.AppendFormatted(m.TotalDuration);
		handler.AppendLiteral(" |");
		stringBuilder12.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## Output");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder13 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Profiles | ");
		handler.AppendFormatted(m.ProfileCount);
		handler.AppendLiteral(" |");
		stringBuilder13.AppendLine(ref handler);
		if (m.StartTime.HasValue)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder14 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("| Sim Start | ");
			handler.AppendFormatted(m.StartTime.Value, "yyyy-MM-dd HH:mm:ss");
			handler.AppendLiteral(" |");
			stringBuilder14.AppendLine(ref handler);
		}
		if (m.EndTime.HasValue)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder15 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
			handler.AppendLiteral("| Sim End | ");
			handler.AppendFormatted(m.EndTime.Value, "yyyy-MM-dd HH:mm:ss");
			handler.AppendLiteral(" |");
			stringBuilder15.AppendLine(ref handler);
		}
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder16 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler.AppendLiteral("## Output Blocks (");
		handler.AppendFormatted(m.OutputBlocks.Count);
		handler.AppendLiteral(")");
		stringBuilder16.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (m.OutputBlocks.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
		}
		else
		{
			foreach (OutputBlockModel outputBlock in m.OutputBlocks)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder17 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(18, 2, stringBuilder2);
				handler.AppendLiteral("- **");
				handler.AppendFormatted(outputBlock.Name);
				handler.AppendLiteral("** (");
				handler.AppendFormatted(outputBlock.ProfileCount);
				handler.AppendLiteral(" profiles)");
				stringBuilder17.AppendLine(ref handler);
			}
		}
		AppendMarkdownSection(stringBuilder, (m.Plan != null) ? PlanInfoArgs.RenderMarkdown(m.Plan) : null);
		AppendMarkdownSection(stringBuilder, (m.Geometry != null) ? GeometryInfoArgs.RenderMarkdown(m.Geometry) : null);
		AppendMarkdownSection(stringBuilder, (m.BoundaryCondition != null) ? BoundaryConditionInfoArgs.RenderMarkdown(m.BoundaryCondition) : null);
		return stringBuilder.ToString().TrimEnd();
	}

	private static void AppendMarkdownSection(StringBuilder sb, string body)
	{
		if (body != null)
		{
			sb.AppendLine();
			sb.AppendLine("---");
			sb.AppendLine();
			sb.AppendLine(body);
		}
	}
}
