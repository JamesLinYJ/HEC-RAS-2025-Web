using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CommandLine;
using H5Assist;
using Ras.Layers;
using Ras.Process.BoundaryConditionCommands;
using Ras.Process.GeometryCommands;
using Ras.Process.Helpers;
using Ras.Process.PlanCommands;
using Ras.Process.ProjectCommands;
using Ras.Process.RasterCommands;
using Ras.Process.ResultCommands;
using Ras.Process.TerrainCommands;
using Utility.Progress;

namespace Ras.Process.Commands;

[Verb("info", false, null, HelpText = "Opens a file, detects its type, and prints info by chaining to the matching 'info' command.")]
public class InfoArgs
{
	public const string Verb = "info";

	[Value(0, MetaName = "source", Required = true, HelpText = "Input file.")]
	public string SourceFile { get; set; }

	[Value(1, Hidden = true)]
	public IEnumerable<string> ExtraArgs { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'name'). Ignores --format.")]
	public string GetPath { get; set; }

	public bool Execute()
	{
		try
		{
			ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
			List<string> stringList = ExtraArgs?.ToList() ?? new List<string>();
			if (stringList.Count > 0)
			{
				progressReporter.ReportMessage($"Received {stringList.Count + 1} positional arguments — only the first is used as the source file.");
				progressReporter.ReportMessage("  source = \"" + SourceFile + "\"");
				for (int i = 0; i < stringList.Count; i++)
				{
					progressReporter.ReportMessage($"  extra[{i}] = \"{stringList[i]}\"");
				}
				progressReporter.ReportMessage("(Tip: a path with spaces or '(' must be wrapped in double quotes, otherwise the shell splits it.)");
				string sourceFile = SourceFile + " " + string.Join(" ", stringList);
				if (!File.Exists(sourceFile))
				{
					return false;
				}
				progressReporter.ReportMessage("Joining all positional arguments produces a real file — proceeding with: \"" + sourceFile + "\"");
				SourceFile = sourceFile;
			}
			if (string.IsNullOrWhiteSpace(SourceFile))
			{
				progressReporter.ReportMessage("No source file specified. (Tip: wrap paths with spaces in double quotes.)");
				return false;
			}
			if (!File.Exists(SourceFile))
			{
				progressReporter.ReportMessage("Source file does not exist: \"" + SourceFile + "\"");
				progressReporter.ReportMessage("(Tip: wrap paths with spaces in double quotes — otherwise the shell splits them into multiple arguments.)");
				return false;
			}
			return Dispatch(SourceFile, progressReporter);
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}

	private bool Dispatch(string fn, ProgressReporter cw)
	{
		switch (Path.GetExtension(fn).ToLowerInvariant())
		{
		case ".ras":
			return new ProjectInfoArgs
			{
				ProjectPath = fn,
				OutputFormat = OutputFormat,
				GetPath = GetPath
			}.Execute();
		case ".h5":
		case ".hdf":
			return DispatchH5(fn, cw);
		default:
			return new RasterInfoArgs
			{
				FilePath = fn,
				OutputFormat = OutputFormat,
				GetPath = GetPath
			}.Execute();
		}
	}

	private bool DispatchH5(string fn, ProgressReporter cw)
	{
		if (Project.IsValidCoreFile(fn, out var fileType))
		{
			switch (fileType)
			{
			case CoreFileType.Geometry:
				return new GeometryInfoArgs
				{
					GeometryFilename = fn,
					OutputFormat = OutputFormat,
					GetPath = GetPath
				}.Execute();
			case CoreFileType.BoundaryCondition:
				return new BoundaryConditionInfoArgs
				{
					BCFilename = fn,
					OutputFormat = OutputFormat,
					GetPath = GetPath
				}.Execute();
			case CoreFileType.Plan:
				return new PlanInfoArgs
				{
					PlanFilename = fn,
					OutputFormat = OutputFormat,
					GetPath = GetPath
				}.Execute();
			case CoreFileType.Result:
				return new ResultInfoArgs
				{
					ResultFilename = fn,
					OutputFormat = OutputFormat,
					GetPath = GetPath
				}.Execute();
			case CoreFileType.Terrain:
				return new TerrainInfoArgs
				{
					TerrainFilename = fn,
					OutputFormat = OutputFormat,
					GetPath = GetPath
				}.Execute();
			}
		}
		H5Info(fn, cw);
		return true;
	}

	private void H5Info(string fn, ProgressReporter pr)
	{
		using H5Reader h5Reader = new H5Reader(fn);
		pr.ReportMessage(FormatFreeformText(fn, "h5", h5Reader.DebugDisplay()));
	}

	private string FormatFreeformText(string filename, string kind, string textValue)
	{
		switch (OutputFormat)
		{
		case OutputFormat.Json:
			return JsonSerializer.Serialize(new
			{
				file = filename,
				kind = kind,
				info = textValue
			}, OutputFormats.Json);
		case OutputFormat.Markdown:
		{
			StringBuilder stringBuilder2;
			StringBuilder stringBuilder = (stringBuilder2 = new StringBuilder());
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 2, stringBuilder2);
			handler.AppendLiteral("# ");
			handler.AppendFormatted(kind);
			handler.AppendLiteral(": `");
			handler.AppendFormatted(filename);
			handler.AppendLiteral("`");
			stringBuilder2.AppendLine(ref handler);
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("```");
			stringBuilder.AppendLine(textValue);
			stringBuilder.AppendLine("```");
			return stringBuilder.ToString().TrimEnd();
		}
		default:
			return textValue;
		}
	}
}
