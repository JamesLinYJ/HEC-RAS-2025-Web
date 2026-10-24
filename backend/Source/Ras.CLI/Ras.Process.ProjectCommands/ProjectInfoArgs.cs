using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.ProjectCommands;

[Verb("info", false, null, HelpText = "Prints information about an existing RAS project.")]
public class ProjectInfoArgs
{
	private sealed class ProjectInfoModel
	{
		public string Filename { get; set; }

		public string FileType { get; set; }

		public string Name { get; set; }

		public string Projection { get; set; }

		public string Units { get; set; }

		public List<LayerEntry> Terrains { get; set; } = new List<LayerEntry>();

		public List<LayerEntry> Geometries { get; set; } = new List<LayerEntry>();

		public List<LayerEntry> Plans { get; set; } = new List<LayerEntry>();

		public List<LayerEntry> Results { get; set; } = new List<LayerEntry>();

		public List<LayerEntry> BoundaryConditions { get; set; } = new List<LayerEntry>();
	}

	private sealed class LayerEntry
	{
		public int Index { get; set; }

		public string Name { get; set; }

		public string Filename { get; set; }
	}

	public const string Verb = "info";

	[Option('p', "project", Required = false, HelpText = "Path to a project (*.ras) or directory containing one. When omitted, the current working directory is searched.")]
	public string ProjectPath { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'name' or 'plans[0].name'). Ignores --format.")]
	public string GetPath { get; set; }

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		string currentDirectory = (string.IsNullOrWhiteSpace(ProjectPath) ? Directory.GetCurrentDirectory() : ProjectPath);
		OperationResult operationResult = Project.TryLoad(currentDirectory, out var prj);
		if (!operationResult)
		{
			Console.WriteLine("Could not load project from " + currentDirectory.InDoubleQuotes() + ": " + operationResult.GetConcatenatedMessages());
			return false;
		}
		ProjectInfoModel projectInfoModel = BuildModel(prj);
		if (!string.IsNullOrEmpty(GetPath))
		{
			return OutputFormats.WriteGet(projectInfoModel, GetPath);
		}
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(projectInfoModel, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(projectInfoModel), 
			OutputFormat.Properties => OutputFormats.Properties(projectInfoModel), 
			_ => RenderText(projectInfoModel), 
		});
		return true;
	}

	private static ProjectInfoModel BuildModel(Project project)
	{
		ProjectInfoModel projectInfoModel = new ProjectInfoModel
		{
			Filename = project.Filename,
			FileType = "RAS 2025 Project",
			Name = project.Name,
			Projection = project.Projection?.GetName(),
			Units = project.Units.ToString()
		};
		AddGroup(projectInfoModel.Terrains, project.Terrains, (Terrain t) => t.Name, (Terrain t) => t.Filename);
		AddGroup(projectInfoModel.Geometries, project.Geometries, (Geometry g) => g.Name, (Geometry g) => g.Filename);
		AddGroup(projectInfoModel.Plans, project.Plans, (Plan p) => p.Name, (Plan p) => p.Filename);
		AddGroup(projectInfoModel.Results, project.Results, (Result r) => r.Name, (Result r) => r.Filename);
		AddGroup(projectInfoModel.BoundaryConditions, project.BoundaryConditions, (BoundaryCondition b) => b.Name, (BoundaryCondition b) => b.Filename);
		return projectInfoModel;
	}

	private static void AddGroup<T>(List<LayerEntry> dest, IReadOnlyList<T> items, Func<T, string> getName, Func<T, string> getFilename)
	{
		for (int i = 0; i < items.Count; i++)
		{
			dest.Add(new LayerEntry
			{
				Index = i,
				Name = getName(items[i]),
				Filename = getFilename(items[i])
			});
		}
	}

	private static string RenderText(ProjectInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename:   " + m.Filename);
		stringBuilder.AppendLine("File Type:  " + m.FileType);
		stringBuilder.AppendLine("Name:       " + m.Name);
		stringBuilder.AppendLine("Projection: " + (m.Projection ?? "(none)"));
		stringBuilder.AppendLine("Units:      " + m.Units);
		AppendTextGroup(stringBuilder, "Terrains", m.Terrains);
		AppendTextGroup(stringBuilder, "Geometries", m.Geometries);
		AppendTextGroup(stringBuilder, "Plans", m.Plans);
		AppendTextGroup(stringBuilder, "Results", m.Results);
		AppendTextGroup(stringBuilder, "Boundary Conditions", m.BoundaryConditions);
		return stringBuilder.ToString().TrimEnd();
	}

	private static void AppendTextGroup(StringBuilder sb, string label, List<LayerEntry> items)
	{
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder);
		handler.AppendFormatted(label);
		handler.AppendLiteral(": ");
		handler.AppendFormatted(items.Count);
		stringBuilder2.AppendLine(ref handler);
		foreach (LayerEntry layerEntry in items)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(9, 3, stringBuilder);
			handler.AppendLiteral("  [");
			handler.AppendFormatted(layerEntry.Index);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(layerEntry.Name);
			handler.AppendLiteral("  (");
			handler.AppendFormatted(layerEntry.Filename);
			handler.AppendLiteral(")");
			stringBuilder3.AppendLine(ref handler);
		}
	}

	private static string RenderMarkdown(ProjectInfoModel m)
	{
		StringBuilder stringBuilder2;
		StringBuilder stringBuilder = (stringBuilder2 = new StringBuilder());
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("# Project: ");
		handler.AppendFormatted(m.Name);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Filename | `");
		handler.AppendFormatted(m.Filename);
		handler.AppendLiteral("` |");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("| File Type | ");
		handler.AppendFormatted(m.FileType);
		handler.AppendLiteral(" |");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("| Name | ");
		handler.AppendFormatted(m.Name);
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Projection | ");
		handler.AppendFormatted(m.Projection ?? "(none)");
		handler.AppendLiteral(" |");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("| Units | ");
		handler.AppendFormatted(m.Units);
		handler.AppendLiteral(" |");
		stringBuilder8.AppendLine(ref handler);
		AppendMarkdownGroup(stringBuilder, "Terrains", m.Terrains);
		AppendMarkdownGroup(stringBuilder, "Geometries", m.Geometries);
		AppendMarkdownGroup(stringBuilder, "Plans", m.Plans);
		AppendMarkdownGroup(stringBuilder, "Results", m.Results);
		AppendMarkdownGroup(stringBuilder, "Boundary Conditions", m.BoundaryConditions);
		return stringBuilder.ToString().TrimEnd();
	}

	private static void AppendMarkdownGroup(StringBuilder sb, string label, List<LayerEntry> items)
	{
		sb.AppendLine();
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 2, stringBuilder);
		handler.AppendLiteral("## ");
		handler.AppendFormatted(label);
		handler.AppendLiteral(" (");
		handler.AppendFormatted(items.Count);
		handler.AppendLiteral(")");
		stringBuilder2.AppendLine(ref handler);
		sb.AppendLine();
		if (items.Count == 0)
		{
			sb.AppendLine("_(none)_");
			return;
		}
		foreach (LayerEntry layerEntry in items)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(14, 3, stringBuilder);
			handler.AppendLiteral("- **[");
			handler.AppendFormatted(layerEntry.Index);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(layerEntry.Name);
			handler.AppendLiteral("** — `");
			handler.AppendFormatted(layerEntry.Filename);
			handler.AppendLiteral("`");
			stringBuilder3.AppendLine(ref handler);
		}
	}
}
