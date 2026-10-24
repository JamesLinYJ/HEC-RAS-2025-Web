using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CommandLine;
using Ras.Process.Helpers;
using Utility.Communication;

namespace Ras.Process.S3Commands;

[Verb("list-bucket", false, null, HelpText = "Lists keys within an S3 bucket. Attempts anonymous access if no credentials are supplied.")]
public class S3ListBucketArgs
{
	private class KeyDetails
	{
		public string Key { get; set; }

		public long? Size { get; set; }

		public DateTime? LastModified { get; set; }

		public string ETag { get; set; }

		public string StorageClass { get; set; }
	}

	public const string Verb = "list-bucket";

	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; } = "https://s3.hecdev.net";

	[Value(0, MetaName = "bucket", Required = true, HelpText = "S3 bucket to list keys from.")]
	public string Bucket { get; set; }

	[Option("prefix", Required = false, HelpText = "If set, only keys with this prefix are listed.")]
	public string Prefix { get; set; } = "";

	[Option('u', "user", Required = false, HelpText = "Username for S3 authentication. Optional - list attempts anonymous access if omitted.")]
	public string User { get; set; }

	[Option('p', "password", Required = false, HelpText = "Password for S3 authentication. Optional - list attempts anonymous access if omitted.")]
	public string Password { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default, one entry per line), Json, or Markdown.")]
	public OutputFormat OutputFormat { get; set; }

	[Option("delimiter", Required = false, HelpText = "Delimiter between entries when --format is Text. Defaults to newline. Accepts escape sequences \\n, \\r, \\t, \\0.")]
	public string Delimiter { get; set; }

	[Option("detailed", Required = false, HelpText = "If set, emits per-key metadata (size, last-modified, etag, storage class) instead of just key names.")]
	public bool Detailed { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		bool useAnonymous = string.IsNullOrEmpty(User) && string.IsNullOrEmpty(Password);
		using S3Client client = (useAnonymous ? new S3Client(Server, forcePathStyle: true) : new S3Client(Server, User, Password, forcePathStyleQueries: true));
		List<S3Object> source;
		try
		{
			source = await client.ListObjectsAsync(Bucket, Prefix);
		}
		catch (AmazonS3Exception ex) when (IsAuthFailure(ex))
		{
			Console.WriteLine($"ERROR: S3 rejected the request ({(int)ex.StatusCode} {ex.StatusCode}).");
			if (useAnonymous)
			{
				Console.WriteLine("       This bucket likely requires authentication. Re-run with --user and --password.");
			}
			else
			{
				Console.WriteLine("       Your credentials may be unauthorized for this bucket.");
			}
			return false;
		}
		string outputFormatJsonTerm;
		if (Detailed)
		{
			List<KeyDetails> keyDetails = source.Select((S3Object o) => new KeyDetails
			{
				Key = o.Key,
				Size = o.Size,
				LastModified = o.LastModified,
				ETag = TrimETag(o.ETag),
				StorageClass = o.StorageClass?.Value
			}).ToList();
			outputFormatJsonTerm = OutputFormat switch
			{
				OutputFormat.Json => JsonSerializer.Serialize(keyDetails, OutputFormats.Json), 
				OutputFormat.Markdown => RenderMarkdownDetailed(keyDetails), 
				_ => RenderTextDetailed(keyDetails, Delimiter), 
			};
		}
		else
		{
			List<string> stringList = source.Select((S3Object o) => o.Key).ToList();
			outputFormatJsonTerm = OutputFormat switch
			{
				OutputFormat.Json => JsonSerializer.Serialize(stringList, OutputFormats.Json), 
				OutputFormat.Markdown => RenderMarkdown(stringList), 
				_ => RenderText(stringList, Delimiter), 
			};
		}
		Console.WriteLine(outputFormatJsonTerm);
		return true;
	}

	private static string RenderText(List<string> keys, string delimiter)
	{
		return string.Join((delimiter == null) ? Environment.NewLine : UnescapeDelimiter(delimiter), keys);
	}

	private static string RenderTextDetailed(List<KeyDetails> entries, string delimiter)
	{
		return string.Join((delimiter == null) ? Environment.NewLine : UnescapeDelimiter(delimiter), entries.Select((KeyDetails e) => $"{e.Key}\tsize={e.Size?.ToString() ?? "-"}\tmodified={FormatDate(e.LastModified)}\tetag={e.ETag ?? "-"}\tclass={e.StorageClass ?? "-"}"));
	}

	private static string RenderMarkdown(List<string> keys)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("# Keys (");
		handler.AppendFormatted(keys.Count);
		handler.AppendLiteral(")");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (keys.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
			return stringBuilder.ToString().TrimEnd();
		}
		foreach (string key in keys)
		{
			stringBuilder.AppendLine("- `" + key.Replace("`", "\\`") + "`");
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderMarkdownDetailed(List<KeyDetails> entries)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("# Keys (");
		handler.AppendFormatted(entries.Count);
		handler.AppendLiteral(")");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (entries.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
			return stringBuilder.ToString().TrimEnd();
		}
		stringBuilder.AppendLine("| Key | Size | Last Modified | ETag | Storage Class |");
		stringBuilder.AppendLine("|-----|------|---------------|------|---------------|");
		foreach (KeyDetails entry in entries)
		{
			string replaceEntryTerm = "`" + (entry.Key ?? "").Replace("`", "\\`").Replace("|", "\\|") + "`";
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 5, stringBuilder2);
			handler.AppendLiteral("| ");
			handler.AppendFormatted(replaceEntryTerm);
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.Size?.ToString() ?? "-");
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(FormatDate(entry.LastModified));
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.ETag ?? "-");
			handler.AppendLiteral(" | ");
			handler.AppendFormatted(entry.StorageClass ?? "-");
			handler.AppendLiteral(" |");
			stringBuilder4.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string FormatDate(DateTime? d)
	{
		if (!d.HasValue)
		{
			return "-";
		}
		return d.Value.ToString("u");
	}

	private static string TrimETag(string etag)
	{
		return etag?.Trim('"');
	}

	private static string UnescapeDelimiter(string s)
	{
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (c == '\\' && i + 1 < s.Length)
			{
				char c2 = s[++i];
				switch (c2)
				{
				case 'n':
					stringBuilder.Append('\n');
					break;
				case 'r':
					stringBuilder.Append('\r');
					break;
				case 't':
					stringBuilder.Append('\t');
					break;
				case '0':
					stringBuilder.Append('\0');
					break;
				case '\\':
					stringBuilder.Append('\\');
					break;
				default:
					stringBuilder.Append('\\').Append(c2);
					break;
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static bool IsAuthFailure(AmazonS3Exception ex)
	{
		if (ex.StatusCode != HttpStatusCode.Forbidden)
		{
			return ex.StatusCode == HttpStatusCode.Unauthorized;
		}
		return true;
	}
}
