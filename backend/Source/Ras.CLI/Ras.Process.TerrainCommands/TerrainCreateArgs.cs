using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using CommandLine.Text;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.TerrainCommands;

[Verb("create", false, null, HelpText = "Create a new empty RAS terrain (*.h5). Use 'terrain add' to add raster sources.")]
public class TerrainCreateArgs
{
	public const string Verb = "create";

	[Option('n', "name", Required = false, HelpText = "Terrain name. Implies project mode: the new terrain is placed in the project's Terrains directory and inherits its projection.")]
	public string Name { get; set; }

	[Option('o', "output", Required = false, HelpText = "Exact destination terrain filename (*.h5). Requires --projection.")]
	public string OutputFilename { get; set; }

	[Option('p', "project", Required = false, HelpText = "Path to a project (*.ras) or a directory containing one. When omitted alongside --name, the current working directory is searched.")]
	public string ProjectPath { get; set; }

	[Option('j', "projection", Required = false, HelpText = "Terrain projection. ESRI .prj filename, EPSG code ('EPSG:900913'), or PROJ string ('PROJ:+proj=aea +lat_0=23 +lat_1=29.5 +lat_2=42.5 +lon_0=-96'). Only valid alongside --output.")]
	public string ProjectionArg { get; set; }

	[Usage(ApplicationAlias = "ras terrain")]
	public static IEnumerable<Example> Examples => new Example[6]
	{
		new Example("Valid - project mode, project discovered in current directory", new TerrainCreateArgs
		{
			Name = "MyTerrain"
		}),
		new Example("Valid - project mode, explicit project path", new TerrainCreateArgs
		{
			Name = "MyTerrain",
			ProjectPath = "./Muncie.ras"
		}),
		new Example("Valid - explicit mode, full path and projection", new TerrainCreateArgs
		{
			OutputFilename = "./MyTerrain.h5",
			ProjectionArg = "EPSG:900913"
		}),
		new Example("Invalid - cannot combine --name with --output (name is derived from filename)", new TerrainCreateArgs
		{
			Name = "MyTerrain",
			OutputFilename = "./MyTerrain.h5"
		}),
		new Example("Invalid - cannot combine --projection with --name or --project (project owns the projection)", new TerrainCreateArgs
		{
			Name = "MyTerrain",
			ProjectPath = "./Muncie.ras",
			ProjectionArg = "EPSG:900913"
		}),
		new Example("Invalid - --output requires --projection (no project context to inherit from)", new TerrainCreateArgs
		{
			OutputFilename = "./MyTerrain.h5"
		})
	};

	public bool Execute()
	{
		bool hasN = !string.IsNullOrWhiteSpace(Name);
		bool hasO = !string.IsNullOrWhiteSpace(OutputFilename);
		bool hasP = !string.IsNullOrWhiteSpace(ProjectPath);
		bool hasJ = !string.IsNullOrWhiteSpace(ProjectionArg);
		if (!TryValidateArgs(hasN, hasO, hasP, hasJ))
		{
			return false;
		}
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		if (!TryResolveTarget(hasN, hasP, hasJ, out var outputFn, out var metadataProject))
		{
			return false;
		}
		if (File.Exists(outputFn))
		{
			Console.WriteLine(outputFn.InDoubleQuotes() + " already exists.");
			return false;
		}
		string directoryName = Path.GetDirectoryName(outputFn);
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			Console.WriteLine("Target directory does not exist: " + directoryName.InDoubleQuotes());
			return false;
		}
		Terrain terrain = Terrain.CreateNew(outputFn);
		terrain.Project = metadataProject;
		terrain.Save();
		Console.WriteLine("Created terrain: " + terrain.Filename);
		Console.WriteLine("Projection:      " + (metadataProject.Projection?.GetName() ?? "(none)"));
		return true;
	}

	private static bool TryValidateArgs(bool hasN, bool hasO, bool hasP, bool hasJ)
	{
		if (!hasN && !hasO)
		{
			Console.WriteLine("Specify either --name (project mode) or --output (explicit mode).");
			return false;
		}
		if (hasN & hasO)
		{
			Console.WriteLine("Cannot specify both --name and --output. The terrain name is derived from the output filename.");
			return false;
		}
		if (hasO & hasP)
		{
			Console.WriteLine("Cannot specify both --output and --project. Project mode places the terrain in the project's Terrains directory.");
			return false;
		}
		if (hasO && !hasJ)
		{
			Console.WriteLine("--output requires --projection. Without a project, the terrain's projection must be specified explicitly.");
			return false;
		}
		if (hasN & hasJ)
		{
			Console.WriteLine("Cannot specify --projection with --name. A project-mode terrain inherits its projection from the project.");
			return false;
		}
		return true;
	}

	private bool TryResolveTarget(bool hasN, bool hasP, bool hasJ, out string outputFn, out Project metadataProject)
	{
		outputFn = null;
		metadataProject = null;
		if (hasN)
		{
			string projectPath = (hasP ? ProjectPath : Directory.GetCurrentDirectory());
			OperationResult operationResult = Project.TryLoad(projectPath, out var prj);
			if (!operationResult)
			{
				Console.WriteLine("Could not load project from " + projectPath.InDoubleQuotes() + ": " + operationResult.GetConcatenatedMessages());
				return false;
			}
			if (prj.Projection == null)
			{
				Console.WriteLine("Project " + prj.Filename.InDoubleQuotes() + " does not have a resolvable projection.");
				return false;
			}
			if (!Directory.Exists(prj.TerrainsDirectory))
			{
				Directory.CreateDirectory(prj.TerrainsDirectory);
			}
			outputFn = Path.Combine(prj.TerrainsDirectory, Name + ".h5");
			metadataProject = prj;
			return true;
		}
		outputFn = OutputFilename;
		if (!CliHelpers.TryResolveProjection(ProjectionArg, out var prj2))
		{
			return false;
		}
		if (prj2 == null)
		{
			Console.WriteLine("Could not resolve projection " + ProjectionArg.InDoubleQuotes());
			return false;
		}
		metadataProject = Project.CreateInMemory();
		metadataProject.ProjectionDefinition = ProjectionDefinition.FromExtractedWKT(prj2);
		return true;
	}
}
