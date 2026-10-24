using System;
using System.Text;
using System.Text.Json;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;

namespace Ras.Process.BoundaryConditionCommands;

[Verb("info", false, null, HelpText = "Prints information about an existing RAS boundary condition file (*.h5).")]
public class BoundaryConditionInfoArgs
{
	internal sealed class BoundaryConditionInfoModel
	{
		public string Filename { get; set; }

		public string FileType { get; set; }

		public string FileVersion { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public bool IsResult { get; set; }

		public string ProjectFilename { get; set; }

		public DateTime StartTime { get; set; }

		public DateTime EndTime { get; set; }

		public TimeSpan Duration { get; set; }

		public int LineCount { get; set; }
	}

	public const string Verb = "info";

	[Value(0, MetaName = "boundarycondition", Required = true, HelpText = "Boundary condition file (*.h5).")]
	public string BCFilename { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'name' or 'lineCount'). Ignores --format.")]
	public string GetPath { get; set; }

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (!TryResolveBoundaryCondition(out var bc))
		{
			return false;
		}
		using (bc)
		{
			BoundaryConditionInfoModel boundaryConditionInfoModel = BuildModel(bc);
			if (!string.IsNullOrEmpty(GetPath))
			{
				return OutputFormats.WriteGet(boundaryConditionInfoModel, GetPath);
			}
			Console.WriteLine(OutputFormat switch
			{
				OutputFormat.Json => JsonSerializer.Serialize(boundaryConditionInfoModel, OutputFormats.Json), 
				OutputFormat.Markdown => RenderMarkdown(boundaryConditionInfoModel), 
				OutputFormat.Properties => OutputFormats.Properties(boundaryConditionInfoModel), 
				_ => RenderText(boundaryConditionInfoModel), 
			});
		}
		return true;
	}

	private bool TryResolveBoundaryCondition(out BoundaryCondition bc)
	{
		return CliHelpers.TryResolveCoreFile(BCFilename, CoreFileType.BoundaryCondition, (string fn) => new BoundaryCondition(fn), out bc);
	}

	internal static BoundaryConditionInfoModel BuildModel(BoundaryCondition bc)
	{
		return new BoundaryConditionInfoModel
		{
			Filename = bc.Filename,
			FileType = "RAS 2025 Boundary Condition",
			FileVersion = bc.GetCurrentFileFormatVersion(),
			Name = bc.Name,
			Description = (string.IsNullOrEmpty(bc.Description) ? null : bc.Description),
			IsResult = bc.IsResult,
			ProjectFilename = bc.Project?.Filename,
			StartTime = bc.StartTime,
			EndTime = bc.EndTime,
			Duration = bc.Duration,
			LineCount = bc.BoundaryConditionLines.Count
		};
	}

	internal static string RenderText(BoundaryConditionInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename:     " + m.Filename);
		stringBuilder.AppendLine("File Type:    " + m.FileType);
		stringBuilder.AppendLine("File Version: " + m.FileVersion);
		stringBuilder.AppendLine("Name:         " + (m.Name ?? "(unknown)"));
		if (m.Description != null)
		{
			stringBuilder.AppendLine("Description:  " + m.Description);
		}
		if (m.IsResult)
		{
			stringBuilder.AppendLine("Embedded in:  Result file");
		}
		stringBuilder.AppendLine("Project:      " + (m.ProjectFilename ?? "(none)"));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Start:        " + m.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("End:          " + m.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("Duration:     " + m.Duration);
		stringBuilder.AppendLine();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("BC Lines:     ");
		handler.AppendFormatted(m.LineCount);
		stringBuilder2.AppendLine(ref handler);
		return stringBuilder.ToString().TrimEnd();
	}

	internal static string RenderMarkdown(BoundaryConditionInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# Boundary Condition: " + (m.Name ?? "(unknown)"));
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
		if (m.Description != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
			handler.AppendLiteral("| Description | ");
			handler.AppendFormatted(m.Description);
			handler.AppendLiteral(" |");
			stringBuilder7.AppendLine(ref handler);
		}
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
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("| Start | ");
		handler.AppendFormatted(m.StartTime, "yyyy-MM-dd HH:mm:ss");
		handler.AppendLiteral(" |");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("| End | ");
		handler.AppendFormatted(m.EndTime, "yyyy-MM-dd HH:mm:ss");
		handler.AppendLiteral(" |");
		stringBuilder10.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Duration | ");
		handler.AppendFormatted(m.Duration);
		handler.AppendLiteral(" |");
		stringBuilder11.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| BC Lines | ");
		handler.AppendFormatted(m.LineCount);
		handler.AppendLiteral(" |");
		stringBuilder12.AppendLine(ref handler);
		return stringBuilder.ToString().TrimEnd();
	}
}
