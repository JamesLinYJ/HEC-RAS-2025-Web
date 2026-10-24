using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Filtering;

namespace Ras.Process.GeometryCommands;

[Verb("list", false, null, HelpText = "Lists topology elements of a RAS geometry file (*.h5) addressed by a selector with an optional inline filter.")]
public class GeometryListArgs
{
	internal sealed class GeometryListModel
	{
		public string Filename { get; set; }

		public string Selector { get; set; }

		public string Element { get; set; }

		public int TotalCount { get; set; }

		public int MatchedCount { get; set; }

		[JsonIgnore]
		public List<string> Columns { get; set; } = new List<string>();

		public List<Dictionary<string, object>> Items { get; set; } = new List<Dictionary<string, object>>();
	}

	public const string Verb = "list";

	[Value(0, MetaName = "geometry", Required = true, HelpText = "Geometry file (*.h5).")]
	public string GeometryFilename { get; set; }

	[Value(1, MetaName = "selector", Required = true, HelpText = "Path to a collection with an optional [filter] and optional trailing field, e.g. \"geometry.topology.region[patchtype = quad AND cellsize < 10]\" or \"topology.arc[cellsize > 0].cellsize\". With no [filter], every element is listed.")]
	public string Selector { get; set; }

	[Option("case-sensitive", Required = false, HelpText = "Make string comparisons in the inline filter case-sensitive.")]
	public bool CaseSensitive { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'matchedCount' or 'items[0].Index'). Ignores --format.")]
	public string GetPath { get; set; }

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (!CliHelpers.TryResolveCoreFile(GeometryFilename, CoreFileType.Geometry, (string fn) => new Geometry(fn), out var result))
		{
			return false;
		}
		FilterOptions options = new FilterOptions
		{
			CaseSensitiveStrings = CaseSensitive
		};
		if (!PathSelector.TryParse(Selector, options, out var selector, out var error))
		{
			Console.Error.WriteLine("Invalid selector: " + error);
			return false;
		}
		if (selector.HasAssignment)
		{
			Console.Error.WriteLine("'list' doesn't assign values - drop the '= value' (use 'modify' to change fields).");
			return false;
		}
		if (!SelectorResolver.TryResolve(new GeometrySelectorNode(result), selector, options, out var resolution, out var error2))
		{
			Console.Error.WriteLine("Invalid selector: " + error2);
			return false;
		}
		if (resolution.HasOperation)
		{
			Console.Error.WriteLine("'list' doesn't modify values - drop the operation (use 'modify' to apply it).");
			return false;
		}
		GeometryListModel geometryListModel = BuildModel(result, resolution);
		if (!string.IsNullOrEmpty(GetPath))
		{
			return OutputFormats.WriteGet(geometryListModel, GetPath);
		}
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(geometryListModel, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(geometryListModel), 
			OutputFormat.Properties => OutputFormats.Properties(geometryListModel), 
			_ => RenderText(geometryListModel), 
		});
		return true;
	}

	private GeometryListModel BuildModel(Geometry geometry, SelectorResolution resolution)
	{
		if (resolution.IsScalar)
		{
			return BuildScalarModel(geometry, resolution);
		}
		ISelectorCollection collection = resolution.Collection;
		List<string> columns;
		if (resolution.TerminalField == null)
		{
			columns = new List<string>(collection.ProjectionFields);
		}
		else
		{
			columns = new List<string> { "Index" };
			if (!string.Equals(resolution.TerminalField, "Index", StringComparison.OrdinalIgnoreCase))
			{
				columns.Add(resolution.TerminalField);
			}
		}
		GeometryListModel geometryListModel = new GeometryListModel
		{
			Filename = geometry.Filename,
			Selector = Selector,
			Element = collection.Name,
			TotalCount = collection.Count,
			MatchedCount = resolution.MatchedIndices.Count,
			Columns = columns
		};
		foreach (int matchedIndex in resolution.MatchedIndices)
		{
			ISelectorElement element = collection.GetElement(matchedIndex);
			Dictionary<string, object> objectByString = new Dictionary<string, object>();
			foreach (string column in columns)
			{
				element.TryGetValue(FilterNames.Normalize(column), out var localValue);
				objectByString[column] = ToCellValue(localValue);
			}
			geometryListModel.Items.Add(objectByString);
		}
		return geometryListModel;
	}

	private GeometryListModel BuildScalarModel(Geometry geometry, SelectorResolution resolution)
	{
		List<string> columns = new List<string> { resolution.TerminalField };
		GeometryListModel geometryListModel = new GeometryListModel
		{
			Filename = geometry.Filename,
			Selector = Selector,
			Element = resolution.ScalarNode.Name,
			TotalCount = 1,
			MatchedCount = 1,
			Columns = columns
		};
		resolution.ScalarNode.Fields.TryGetValue(resolution.TerminalFieldNormalized, out var localValue);
		geometryListModel.Items.Add(new Dictionary<string, object> { 
		{
			resolution.TerminalField,
			ToCellValue(localValue)
		} });
		return geometryListModel;
	}

	private static object ToCellValue(object localValue)
	{
		if (localValue == null)
		{
			return null;
		}
		if (localValue is float computed)
		{
			if (!float.IsNaN(computed))
			{
				return computed;
			}
			return null;
		}
		if (localValue is double computed2)
		{
			if (!double.IsNaN(computed2))
			{
				return computed2;
			}
			return null;
		}
		if (localValue is Enum)
		{
			return localValue.ToString();
		}
		if (localValue is bool || localValue is string || localValue is int || localValue is long)
		{
			return localValue;
		}
		return Convert.ToString(localValue, CultureInfo.InvariantCulture);
	}

	internal static string RenderText(GeometryListModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename: " + m.Filename);
		stringBuilder.AppendLine("Selector: " + m.Selector);
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 2, stringBuilder2);
		handler.AppendLiteral("Matched:  ");
		handler.AppendFormatted(m.MatchedCount);
		handler.AppendLiteral(" of ");
		handler.AppendFormatted(m.TotalCount);
		stringBuilder2.AppendLine(ref handler);
		if (m.Items.Count == 0)
		{
			return stringBuilder.ToString().TrimEnd();
		}
		stringBuilder.AppendLine();
		AppendTextTable(stringBuilder, m.Columns, m.Items);
		return stringBuilder.ToString().TrimEnd();
	}

	private static void AppendTextTable(StringBuilder sb, List<string> columns, List<Dictionary<string, object>> items)
	{
		int[] intValues = new int[columns.Count];
		for (int i = 0; i < columns.Count; i++)
		{
			intValues[i] = columns[i].Length;
		}
		string[][] strings = new string[items.Count][];
		for (int j = 0; j < items.Count; j++)
		{
			strings[j] = new string[columns.Count];
			for (int k = 0; k < columns.Count; k++)
			{
				items[j].TryGetValue(columns[k], out var localValue);
				string cellText = CellText(localValue);
				strings[j][k] = cellText;
				intValues[k] = Math.Max(intValues[k], cellText.Length);
			}
		}
		for (int l = 0; l < columns.Count; l++)
		{
			sb.Append(columns[l].PadRight(intValues[l])).Append((l == columns.Count - 1) ? "" : "  ");
		}
		sb.AppendLine();
		for (int m = 0; m < items.Count; m++)
		{
			for (int n = 0; n < columns.Count; n++)
			{
				sb.Append(strings[m][n].PadRight(intValues[n])).Append((n == columns.Count - 1) ? "" : "  ");
			}
			sb.AppendLine();
		}
	}

	internal static string RenderMarkdown(GeometryListModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
		handler.AppendLiteral("# ");
		handler.AppendFormatted(m.Element);
		handler.AppendLiteral(": ");
		handler.AppendFormatted(m.Filename);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("Selector: `");
		handler.AppendFormatted(m.Selector);
		handler.AppendLiteral("`");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 2, stringBuilder2);
		handler.AppendLiteral("Matched: ");
		handler.AppendFormatted(m.MatchedCount);
		handler.AppendLiteral(" of ");
		handler.AppendFormatted(m.TotalCount);
		stringBuilder5.AppendLine(ref handler);
		if (m.Items.Count == 0)
		{
			return stringBuilder.ToString().TrimEnd();
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| " + string.Join(" | ", m.Columns) + " |");
		stringBuilder.AppendLine(string.Concat("|", string.Concat(Enumerable.Repeat("---|", m.Columns.Count))));
		foreach (Dictionary<string, object> localValue in m.Items)
		{
			stringBuilder.Append('|');
			foreach (string column in m.Columns)
			{
				localValue.TryGetValue(column, out var localValue2);
				stringBuilder.Append(' ').Append(CellText(localValue2)).Append(" |");
			}
			stringBuilder.AppendLine();
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string CellText(object localValue)
	{
		if (localValue == null)
		{
			return "";
		}
		if (localValue is double computed)
		{
			return computed.ToString("0.###", CultureInfo.InvariantCulture);
		}
		if (localValue is float computed2)
		{
			return computed2.ToString("0.###", CultureInfo.InvariantCulture);
		}
		return Convert.ToString(localValue, CultureInfo.InvariantCulture) ?? "";
	}
}
