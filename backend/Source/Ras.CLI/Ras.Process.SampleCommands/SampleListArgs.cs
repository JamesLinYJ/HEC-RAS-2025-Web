using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommandLine;
using Ras.Process.Helpers;
using Ras.Updates;

namespace Ras.Process.SampleCommands;

[Verb("list", false, null, HelpText = "Lists all sample projects available in the global example-project manifest.")]
public class SampleListArgs
{
	public const string Verb = "list";

	[Option('t', "tag", Required = false, HelpText = "If set, only lists samples that include this tag (case-insensitive). May be specified multiple times; a sample matches if any tag matches.")]
	public IEnumerable<string> Tags { get; set; }

	[Option("filter", Required = false, HelpText = "Substring filter (case-insensitive) matched against the sample title and description. Combines with --tag.")]
	public string Filter { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, or Markdown.")]
	public OutputFormat OutputFormat { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		ExampleProjectCollection exampleProjectCollection = await ExampleProjectHelper.TryGetAllExamplesAsync();
		if (exampleProjectCollection == null)
		{
			Console.WriteLine("ERROR: Could not retrieve the global example-project manifest from S3.");
			return false;
		}
		List<ExampleProjectDetails> projects = FilterByText(FilterByTags(exampleProjectCollection.AllProjects ?? new List<ExampleProjectDetails>(), Tags), Filter);
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => RenderJson(projects), 
			OutputFormat.Markdown => RenderMarkdown(projects), 
			_ => RenderText(projects), 
		});
		return true;
	}

	private static List<ExampleProjectDetails> FilterByTags(List<ExampleProjectDetails> all, IEnumerable<string> tags)
	{
		if (tags == null)
		{
			return all;
		}
		List<string> tagList = tags.Where((string t) => !string.IsNullOrWhiteSpace(t)).ToList();
		if (tagList.Count == 0)
		{
			return all;
		}
		return all.Where((ExampleProjectDetails p) => p.Tags != null && p.Tags.Any((string pt) => tagList.Any((string qt) => string.Equals(pt, qt, StringComparison.OrdinalIgnoreCase)))).ToList();
	}

	private static List<ExampleProjectDetails> FilterByText(List<ExampleProjectDetails> all, string filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return all;
		}
		return all.Where((ExampleProjectDetails p) => (p.Title != null && p.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)) || (p.Description != null && p.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
	}

	private static string RenderText(List<ExampleProjectDetails> projects)
	{
		if (projects.Count == 0)
		{
			return "No sample projects found.";
		}
		int projectsExampleProjectDetailsTerm = Math.Min(40, Math.Max(5, projects.Max((ExampleProjectDetails p) => (p.Title ?? "").Length)));
		int projectsExampleProjectDetailsTerm2 = Math.Min(30, Math.Max(0, projects.Max((ExampleProjectDetails p) => FormatTags(p.Tags).Length)));
		int width3 = (TryGetConsoleWidth(out var width) ? width : 120);
		int ordinal = projectsExampleProjectDetailsTerm + 2 + 10 + 2 + projectsExampleProjectDetailsTerm2 + 2;
		int width2 = Math.Max(10, width3 - ordinal - 1);
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("Sample projects: ");
		handler.AppendFormatted(projects.Count);
		stringBuilder3.AppendLine(ref handler);
		foreach (ExampleProjectDetails project in projects)
		{
			string padRight = Truncate(project.Title ?? "", projectsExampleProjectDetailsTerm).PadRight(projectsExampleProjectDetailsTerm);
			string publishDate = project.PublishDate.ToString("yyyy-MM-dd");
			string padRight2 = Truncate(FormatTags(project.Tags), projectsExampleProjectDetailsTerm2).PadRight(projectsExampleProjectDetailsTerm2);
			string replaceDescriptionTerm = Truncate((project.Description ?? "").Replace('\n', ' ').Replace('\r', ' '), width2);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(6, 4, stringBuilder2);
			handler.AppendFormatted(padRight);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(publishDate);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(padRight2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(replaceDescriptionTerm);
			stringBuilder4.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string FormatTags(List<string> tags)
	{
		if (tags != null && tags.Count != 0)
		{
			return string.Join(",", tags);
		}
		return "";
	}

	private static string Truncate(string s, int width)
	{
		if (s.Length <= width)
		{
			return s;
		}
		if (width <= 1)
		{
			return s.Substring(0, width);
		}
		return s.Substring(0, width - 1) + "…";
	}

	private static bool TryGetConsoleWidth(out int width)
	{
		width = 0;
		try
		{
			width = Console.WindowWidth;
			return width > 0;
		}
		catch
		{
			return false;
		}
	}

	private static string RenderMarkdown(List<ExampleProjectDetails> projects)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("# Sample projects (");
		handler.AppendFormatted(projects.Count);
		handler.AppendLiteral(")");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (projects.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
			return stringBuilder.ToString().TrimEnd();
		}
		stringBuilder.AppendLine("| Title | Published | Tags | Description |");
		stringBuilder.AppendLine("|-------|-----------|------|-------------|");
		foreach (ExampleProjectDetails project in projects)
		{
			string tagsProjectTerm = ((project.Tags != null) ? string.Join(", ", project.Tags) : "");
			string replace = (project.Description ?? "").Replace("|", "\\|");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 4, stringBuilder2);
			handler.AppendLiteral("| ");
			handler.AppendFormatted(project.Title);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(project.PublishDate, "yyyy-MM-dd");
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(tagsProjectTerm);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(replace);
			handler.AppendLiteral(" |");
			stringBuilder4.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderJson(List<ExampleProjectDetails> projects)
	{
		return JsonSerializer.Serialize(projects, OutputFormats.Json);
	}
}
