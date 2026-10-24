using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.ProjectCommands;

internal static class ProjectListHelper
{
	public static bool Run<T>(string projectPath, ProjectListField field, string delimiter, OutputFormat outputFormat, Func<Project, IReadOnlyList<T>> selector, Func<T, string> getName, Func<T, string> getFilename)
	{
		string currentDirectory = (string.IsNullOrWhiteSpace(projectPath) ? Directory.GetCurrentDirectory() : projectPath);
		OperationResult operationResult = Project.TryLoad(currentDirectory, out var prj);
		if (!operationResult)
		{
			Console.WriteLine("Could not load project from " + currentDirectory.InDoubleQuotes() + ": " + operationResult.GetConcatenatedMessages());
			return false;
		}
		IReadOnlyList<T> items = selector(prj);
		List<string> extractFieldValues = ExtractFieldValues(prj, items, field, getName, getFilename);
		Console.WriteLine(outputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(extractFieldValues, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(extractFieldValues), 
			_ => string.Join(ResolveDelimiter(delimiter), extractFieldValues), 
		});
		return true;
	}

	private static List<string> ExtractFieldValues<T>(Project project, IReadOnlyList<T> items, ProjectListField field, Func<T, string> getName, Func<T, string> getFilename)
	{
		List<string> stringList = new List<string>(items.Count);
		string projectDirectory = project.ProjectDirectory;
		foreach (T localValue in items)
		{
			switch (field)
			{
			case ProjectListField.Absolute:
				stringList.Add(getFilename(localValue));
				break;
			case ProjectListField.Relative:
			{
				string getFilename2 = getFilename(localValue);
				stringList.Add(string.IsNullOrEmpty(projectDirectory) ? getFilename2 : Path.GetRelativePath(projectDirectory, getFilename2));
				break;
			}
			default:
				stringList.Add(getName(localValue));
				break;
			}
		}
		return stringList;
	}

	private static string ResolveDelimiter(string raw)
	{
		if (raw == null)
		{
			return Environment.NewLine;
		}
		StringBuilder stringBuilder = new StringBuilder(raw.Length);
		for (int i = 0; i < raw.Length; i++)
		{
			char c = raw[i];
			if (c == '\\' && i + 1 < raw.Length)
			{
				char c2 = raw[++i];
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

	private static string RenderMarkdown(List<string> values)
	{
		if (values.Count == 0)
		{
			return "_(none)_";
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string textValue in values)
		{
			stringBuilder.AppendLine("- " + textValue);
		}
		return stringBuilder.ToString().TrimEnd();
	}
}
