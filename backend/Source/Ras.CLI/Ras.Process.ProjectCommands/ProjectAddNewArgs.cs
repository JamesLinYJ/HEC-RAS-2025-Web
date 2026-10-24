using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.ProjectCommands;

[Verb("add-new", false, null, HelpText = "Creates a brand-new layer file inside the project (terrain, geometry, plan, or boundary condition).")]
public class ProjectAddNewArgs
{
	public const string Verb = "add-new";

	[Option('p', "project", Required = false, HelpText = "Path to the target project (*.ras) or directory containing one. When omitted, the current working directory is searched.")]
	public string ProjectPath { get; set; }

	[Option('t', "type", Required = true, HelpText = "Layer type to create. One of: terrain, geometry, plan, boundarycondition (alias 'bc'). Results are not user-creatable.")]
	public string TypeArg { get; set; }

	[Option('n', "name", Required = true, HelpText = "Layer name. Becomes the filename basename (e.g. 'MyTerrain' -> '<project>/Terrains/MyTerrain.h5').")]
	public string Name { get; set; }

	public bool Execute()
	{
		if (!CliHelpers.TryParseCoreFileType(TypeArg, out var type))
		{
			Console.WriteLine("Unknown --type: " + TypeArg.InDoubleQuotes() + " (expected one of: terrain, geometry, plan, boundarycondition).");
			return false;
		}
		if (type == CoreFileType.Result)
		{
			Console.WriteLine("Cannot create a Result file - results are produced by the solver, not user-created.");
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
		string destinationDirectory = GetDestinationDirectory(prj, type);
		if (!Directory.Exists(destinationDirectory))
		{
			Directory.CreateDirectory(destinationDirectory);
		}
		string path = Path.Combine(destinationDirectory, Name + ".h5");
		if (File.Exists(path))
		{
			Console.WriteLine(path.InDoubleQuotes() + " already exists.");
			return false;
		}
		CoreLayerBase coreLayerBase = CreateLayer(type, path);
		coreLayerBase.Project = prj;
		coreLayerBase.Save();
		Console.WriteLine($"Created {type}: {path}");
		return true;
	}

	private static string GetDestinationDirectory(Project project, CoreFileType type)
	{
		return type switch
		{
			CoreFileType.Terrain => project.TerrainsDirectory, 
			CoreFileType.Geometry => project.GeometriesDirectory, 
			CoreFileType.Plan => project.PlansDirectory, 
			CoreFileType.BoundaryCondition => project.BoundaryConditionDirectory, 
			_ => throw new NotSupportedException("Unsupported layer type: " + type), 
		};
	}

	private static CoreLayerBase CreateLayer(CoreFileType type, string filename)
	{
		return type switch
		{
			CoreFileType.Terrain => (CoreLayerBase)Terrain.CreateNew(filename), 
			CoreFileType.Geometry => Geometry.CreateNew(filename), 
			CoreFileType.Plan => Plan.CreateNew(filename), 
			CoreFileType.BoundaryCondition => BoundaryCondition.CreateNew(filename), 
			_ => throw new NotSupportedException("Unsupported layer type: " + type), 
		};
	}
}
