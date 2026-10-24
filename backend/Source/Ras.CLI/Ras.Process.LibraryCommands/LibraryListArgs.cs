using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using CommandLine;
using Ras.ModelLibrary;
using Ras.Process.Helpers;
using Utility.Files;

namespace Ras.Process.LibraryCommands;

[Verb("list", false, null, HelpText = "Lists the models in the developer model library.")]
public class LibraryListArgs : LibraryCommandBase
{
	public const string Verb = "list";

	[Option("prefix", Required = false, HelpText = "If set, only lists models whose ID starts with this prefix (e.g. 'alex/').")]
	public string Prefix { get; set; }

	[Option("filter", Required = false, HelpText = "Substring filter (case-insensitive) matched against the model ID and owner.")]
	public string Filter { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, or Markdown.")]
	public OutputFormat OutputFormat { get; set; }

	[Option("count", Required = false, HelpText = "Print only the number of matching models, not the listing. Honors --prefix and --filter.")]
	public bool CountOnly { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		if (!TryCreateClient(out var client))
		{
			return false;
		}
		using (client)
		{
			List<ModelLibraryEntry> all;
			try
			{
				all = await client.ListAsync(Prefix ?? "");
			}
			catch (AmazonS3Exception ex) when (LibraryCommandBase.IsAuthFailure(ex))
			{
				return LibraryCommandBase.ReportAuthFailure(ex);
			}
			List<ModelLibraryEntry> filterByText = FilterByText(all, Filter);
			if (CountOnly)
			{
				Console.WriteLine(filterByText.Count);
				return true;
			}
			List<ModelLibraryEntry> orderBy = filterByText.OrderBy((ModelLibraryEntry e) => e.Id, StringComparer.OrdinalIgnoreCase).ToList();
			Console.WriteLine(OutputFormat switch
			{
				OutputFormat.Json => JsonSerializer.Serialize(orderBy, OutputFormats.Json), 
				OutputFormat.Markdown => RenderMarkdown(orderBy), 
				_ => RenderText(orderBy), 
			});
			return true;
		}
	}

	private static List<ModelLibraryEntry> FilterByText(List<ModelLibraryEntry> all, string filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return all;
		}
		return all.Where((ModelLibraryEntry e) => (e.Id != null && e.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)) || (e.Owner != null && e.Owner.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
	}

	private static string RenderText(List<ModelLibraryEntry> entries)
	{
		if (entries.Count == 0)
		{
			return "No models found.";
		}
		int totalWidth = Math.Max(2, entries.Max((ModelLibraryEntry e) => (e.Id ?? "").Length));
		int entriesModelLibraryEntryTerm = Math.Min(24, Math.Max(5, entries.Max((ModelLibraryEntry e) => (e.Owner ?? "").Length)));
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder2);
		handler.AppendLiteral("Models: ");
		handler.AppendFormatted(entries.Count);
		stringBuilder3.AppendLine(ref handler);
		foreach (ModelLibraryEntry entry in entries)
		{
			string padRight = (entry.Id ?? "").PadRight(totalWidth);
			string padRight2 = Truncate(entry.Owner ?? "", entriesModelLibraryEntryTerm).PadRight(entriesModelLibraryEntryTerm);
			string padLeft = FileHelpers.PrettyByteCount(entry.SizeBytes).PadLeft(10);
			string lastModified = entry.LastModified.ToString("yyyy-MM-dd HH:mm");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(6, 4, stringBuilder2);
			handler.AppendFormatted(padRight);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(padRight2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(padLeft);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(lastModified);
			stringBuilder4.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderMarkdown(List<ModelLibraryEntry> entries)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("# Models (");
		handler.AppendFormatted(entries.Count);
		handler.AppendLiteral(")");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (entries.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
			return stringBuilder.ToString().TrimEnd();
		}
		stringBuilder.AppendLine("| ID | Owner | Size | Last Modified |");
		stringBuilder.AppendLine("|----|-------|------|---------------|");
		foreach (ModelLibraryEntry entry in entries)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 4, stringBuilder2);
			handler.AppendLiteral("| ");
			handler.AppendFormatted(entry.Id);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.Owner);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(FileHelpers.PrettyByteCount(entry.SizeBytes));
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.LastModified, "yyyy-MM-dd HH:mm");
			handler.AppendLiteral(" |");
			stringBuilder4.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
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
}
