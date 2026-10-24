using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.ProjectCommands;

[Verb("add", false, null, HelpText = "Adds an existing layer file (*.h5) to a project by copying it into the project's appropriate subdirectory. The layer type is detected from the file.")]
public class ProjectAddArgs
{
	public const string Verb = "add";

	[Option('p', "project", Required = false, HelpText = "Path to the target project (*.ras) or directory containing one. When omitted, the current working directory is searched.")]
	public string ProjectPath { get; set; }

	[Option('f', "file", Required = true, HelpText = "Source layer file (*.h5) to add. Layer type (Terrain/Geometry/Plan/Result/BoundaryCondition) is detected from the file contents.")]
	public string SourceFile { get; set; }

	public bool Execute()
	{
		if (!File.Exists(SourceFile))
		{
			Console.WriteLine(SourceFile.InDoubleQuotes() + " does not exist.");
			return false;
		}
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		string currentDirectory = (string.IsNullOrWhiteSpace(ProjectPath) ? Directory.GetCurrentDirectory() : ProjectPath);
		OperationResult operationResult = Project.TryLoad(currentDirectory, out var prj);
		if (!operationResult)
		{
			Console.WriteLine("Could not load project from " + currentDirectory.InDoubleQuotes() + ": " + operationResult.GetConcatenatedMessages());
			return false;
		}
		if (!Project.IsValidCoreFile(SourceFile, out var fileType))
		{
			Console.WriteLine(SourceFile.InDoubleQuotes() + " is not a recognized RAS core file (Terrain/Geometry/Plan/Result/BoundaryCondition).");
			return false;
		}
		string destinationDirectory = GetDestinationDirectory(prj, fileType);
		if (!Directory.Exists(destinationDirectory))
		{
			Directory.CreateDirectory(destinationDirectory);
		}
		string path = Path.Combine(destinationDirectory, Path.GetFileName(SourceFile));
		if (File.Exists(path))
		{
			Console.WriteLine(path.InDoubleQuotes() + " already exists in the project.");
			return false;
		}
		File.Copy(SourceFile, path);
		Console.WriteLine($"Added {fileType}: {path}");
		return true;
	}

	private static string GetDestinationDirectory(Project project, CoreFileType type)
	{
		return type switch
		{
			CoreFileType.Terrain => project.TerrainsDirectory, 
			CoreFileType.Geometry => project.GeometriesDirectory, 
			CoreFileType.Plan => project.PlansDirectory, 
			CoreFileType.Result => project.ResultsDirectory, 
			CoreFileType.BoundaryCondition => project.BoundaryConditionDirectory, 
			_ => throw new NotSupportedException("Unsupported layer type: " + type), 
		};
	}
}
