using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using CommandLine.Text;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;

namespace Ras.Process.ProjectCommands;

[Verb("create", false, null, HelpText = "Create a new empty RAS project (*.ras). Sets up the standard subdirectory layout (Terrains, Geometries, Plans, Results, Boundary Conditions).")]
public class ProjectCreateArgs
{
	public const string Verb = "create";

	[Option('o', "output", Required = true, HelpText = "Destination project filename, e.g. './MyProject.ras'. The project name is the filename basename. Parent directory must exist.")]
	public string OutputFilename { get; set; }

	[Option('j', "projection", Required = true, HelpText = "Project projection. ESRI .prj filename, EPSG code ('EPSG:900913'), or PROJ string ('PROJ:+proj=aea ...').")]
	public string ProjectionArg { get; set; }

	[Usage(ApplicationAlias = "ras project")]
	public static IEnumerable<Example> Examples => new Example[2]
	{
		new Example("Create a project with an EPSG projection", new ProjectCreateArgs
		{
			OutputFilename = "./MyProject.ras",
			ProjectionArg = "EPSG:900913"
		}),
		new Example("Create a project from an ESRI .prj file", new ProjectCreateArgs
		{
			OutputFilename = "./MyProject.ras",
			ProjectionArg = "./projection.prj"
		})
	};

	public bool Execute()
	{
		if (File.Exists(OutputFilename))
		{
			Console.WriteLine(OutputFilename.InDoubleQuotes() + " already exists.");
			return false;
		}
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(OutputFilename));
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			Console.WriteLine("Target directory does not exist: " + directoryName.InDoubleQuotes());
			return false;
		}
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		if (!CliHelpers.TryResolveProjection(ProjectionArg, out var prj))
		{
			return false;
		}
		if (prj == null)
		{
			Console.WriteLine("Could not resolve projection " + ProjectionArg.InDoubleQuotes());
			return false;
		}
		ProjectionDefinition projection = ProjectionDefinition.FromExtractedWKT(prj);
		Project project = Project.CreateNew(OutputFilename, projection);
		Console.WriteLine("Created project: " + project.Filename);
		Console.WriteLine("Name:            " + project.Name);
		Console.WriteLine("Projection:      " + (project.Projection?.GetName() ?? "(none)"));
		return true;
	}
}
