using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommandLine;
using Ras.Process.Helpers;
using Ras.Updates;
using Utility.Extensions;
using Utility.Files;

namespace Ras.Process.SampleCommands;

[Verb("info", false, null, HelpText = "Prints details about a single sample project (manifest, files, total size).")]
public class SampleInfoArgs
{
	public const string Verb = "info";

	[Value(0, MetaName = "title", Required = true, HelpText = "Title of the sample project, as shown by 'ras sample list'. Matched case-insensitively. If multiple entries share the title, the most recently published is used.")]
	public string Title { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'details.title' or 'files[0].filename'). Ignores --format.")]
	public string GetPath { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		SampleResolver.ResolveResult resolveResult = await SampleResolver.TryResolveAsync(Title, "sample info");
		if (!resolveResult.Success)
		{
			return false;
		}
		ExampleProjectDetails details = resolveResult.Details;
		ExampleProjectManifest exampleProjectManifest = await ExampleProjectHelper.TryGetExampleManifestAsync(details);
		if (exampleProjectManifest == null)
		{
			Console.WriteLine("ERROR: Could not retrieve the sample manifest from S3 (key " + details.ManifestJSONKey.InSingleQuotes() + ").");
			return false;
		}
		object fileTypeDetailsTerm = new
		{
			FileType = "Sample Project",
			Details = details,
			Files = exampleProjectManifest.Files
		};
		if (!string.IsNullOrEmpty(GetPath))
		{
			return OutputFormats.WriteGet(fileTypeDetailsTerm, GetPath);
		}
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(fileTypeDetailsTerm, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(details, exampleProjectManifest), 
			OutputFormat.Properties => OutputFormats.Properties(fileTypeDetailsTerm), 
			_ => RenderText(details, exampleProjectManifest), 
		});
		return true;
	}

	private static string RenderText(ExampleProjectDetails d, ExampleProjectManifest m)
	{
		long numBytes = m.Files.Sum((MappedFile f) => f.Length);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Title:        " + d.Title);
		stringBuilder.AppendLine("File Type:    Sample Project");
		stringBuilder.AppendLine("Published:    " + d.PublishDate.ToString("yyyy-MM-dd HH:mm"));
		if (!string.IsNullOrWhiteSpace(d.Description))
		{
			stringBuilder.AppendLine("Description:  " + d.Description);
		}
		if (d.Tags != null && d.Tags.Count > 0)
		{
			stringBuilder.AppendLine("Tags:         " + string.Join(", ", d.Tags));
		}
		if (!string.IsNullOrWhiteSpace(d.MinimumVersion))
		{
			stringBuilder.AppendLine("MinVersion:   " + d.MinimumVersion);
		}
		if (!string.IsNullOrWhiteSpace(d.MaximumVersion))
		{
			stringBuilder.AppendLine("MaxVersion:   " + d.MaximumVersion);
		}
		stringBuilder.AppendLine("Server:       " + d.Server);
		stringBuilder.AppendLine("Bucket:       " + d.Bucket);
		stringBuilder.AppendLine("Manifest Key: " + d.ManifestJSONKey);
		stringBuilder.AppendLine("Files:        " + m.Files.Count + "  (" + FileHelpers.PrettyByteCount(numBytes) + ")");
		foreach (MappedFile file in m.Files)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(file.Filename);
			handler.AppendLiteral("  (");
			handler.AppendFormatted(FileHelpers.PrettyByteCount(file.Length));
			handler.AppendLiteral(")");
			stringBuilder2.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderMarkdown(ExampleProjectDetails d, ExampleProjectManifest m)
	{
		long numBytes = m.Files.Sum((MappedFile f) => f.Length);
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("# Sample: ");
		handler.AppendFormatted(d.Title);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder.AppendLine("| File Type | Sample Project |");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("| Published | ");
		handler.AppendFormatted(d.PublishDate, "yyyy-MM-dd HH:mm");
		handler.AppendLiteral(" |");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
		handler.AppendLiteral("| Description | ");
		handler.AppendFormatted((d.Description ?? "").Replace("|", "\\|"));
		handler.AppendLiteral(" |");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("| Tags | ");
		handler.AppendFormatted((d.Tags != null) ? string.Join(", ", d.Tags) : "");
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| MinVersion | ");
		handler.AppendFormatted(d.MinimumVersion);
		handler.AppendLiteral(" |");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| MaxVersion | ");
		handler.AppendFormatted(d.MaximumVersion);
		handler.AppendLiteral(" |");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Server | `");
		handler.AppendFormatted(d.Server);
		handler.AppendLiteral("` |");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("| Bucket | `");
		handler.AppendFormatted(d.Bucket);
		handler.AppendLiteral("` |");
		stringBuilder10.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(21, 1, stringBuilder2);
		handler.AppendLiteral("| Manifest Key | `");
		handler.AppendFormatted(d.ManifestJSONKey);
		handler.AppendLiteral("` |");
		stringBuilder11.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 2, stringBuilder2);
		handler.AppendLiteral("## Files (");
		handler.AppendFormatted(m.Files.Count);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(FileHelpers.PrettyByteCount(numBytes));
		handler.AppendLiteral(")");
		stringBuilder12.AppendLine(ref handler);
		stringBuilder.AppendLine();
		foreach (MappedFile file in m.Files)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder2);
			handler.AppendLiteral("- `");
			handler.AppendFormatted(file.Filename);
			handler.AppendLiteral("` (");
			handler.AppendFormatted(FileHelpers.PrettyByteCount(file.Length));
			handler.AppendLiteral(")");
			stringBuilder13.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}
}
