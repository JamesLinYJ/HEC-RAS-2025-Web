using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using CommandLine;
using Ras.ModelLibrary;
using Ras.Process.Helpers;
using Utility.Files;

namespace Ras.Process.LibraryCommands;

[Verb("info", false, null, HelpText = "Prints details about a single model in the library (owner, size, last modified).")]
public class LibraryInfoArgs : LibraryCommandBase
{
	public const string Verb = "info";

	[Value(0, MetaName = "id", Required = true, HelpText = "ID of the model, as shown by 'ras library list'. Matched case-insensitively.")]
	public string Id { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value at a flattened key path (e.g. 'owner' or 'sizeBytes'). Ignores --format.")]
	public string GetPath { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		if (!TryCreateClient(out var client))
		{
			return false;
		}
		using (client)
		{
			List<ModelLibraryEntry> listAsync;
			try
			{
				listAsync = await client.ListAsync(Id);
			}
			catch (AmazonS3Exception ex) when (LibraryCommandBase.IsAuthFailure(ex))
			{
				return LibraryCommandBase.ReportAuthFailure(ex);
			}
			ModelLibraryEntry? modelLibraryEntry = null;
			foreach (ModelLibraryEntry listAsync2 in listAsync)
			{
				if (string.Equals(listAsync2.Id, Id, StringComparison.OrdinalIgnoreCase))
				{
					modelLibraryEntry = listAsync2;
					break;
				}
			}
			if (!modelLibraryEntry.HasValue)
			{
				Console.WriteLine("ERROR: No model with ID '" + Id + "' found in the library.");
				return false;
			}
			ModelLibraryEntry modelLibraryEntry2 = modelLibraryEntry.Value;
			if (!string.IsNullOrEmpty(GetPath))
			{
				return OutputFormats.WriteGet(modelLibraryEntry2, GetPath);
			}
			Console.WriteLine(OutputFormat switch
			{
				OutputFormat.Json => JsonSerializer.Serialize(modelLibraryEntry2, OutputFormats.Json), 
				OutputFormat.Markdown => RenderMarkdown(modelLibraryEntry2), 
				OutputFormat.Properties => OutputFormats.Properties(modelLibraryEntry2), 
				_ => RenderText(modelLibraryEntry2), 
			});
			return true;
		}
	}

	private static string RenderText(ModelLibraryEntry e)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("ID:            " + e.Id);
		stringBuilder.AppendLine("Owner:         " + e.Owner);
		stringBuilder.AppendLine("Size:          " + FileHelpers.PrettyByteCount(e.SizeBytes));
		stringBuilder.AppendLine("Last Modified: " + e.LastModified.ToString("yyyy-MM-dd HH:mm"));
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderMarkdown(ModelLibraryEntry e)
	{
		StringBuilder stringBuilder2;
		StringBuilder stringBuilder = (stringBuilder2 = new StringBuilder());
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("# Model: ");
		handler.AppendFormatted(e.Id);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("| Owner | ");
		handler.AppendFormatted(e.Owner);
		handler.AppendLiteral(" |");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("| Size | ");
		handler.AppendFormatted(FileHelpers.PrettyByteCount(e.SizeBytes));
		handler.AppendLiteral(" |");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("| Last Modified | ");
		handler.AppendFormatted(e.LastModified, "yyyy-MM-dd HH:mm");
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		return stringBuilder.ToString().TrimEnd();
	}
}
