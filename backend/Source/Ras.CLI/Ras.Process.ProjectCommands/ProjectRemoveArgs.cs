using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.ProjectCommands;

[Verb("remove", false, null, HelpText = "Removes a layer from a project by name. Deletes the underlying *.h5 file from disk.")]
public class ProjectRemoveArgs
{
	public const string Verb = "remove";

	[Option('p', "project", Required = true, HelpText = "Path to the target project (*.ras) or directory containing one.")]
	public string ProjectPath { get; set; }

	[Option('n', "name", Required = true, HelpText = "Name of the layer to remove (matches against the layer's Name property).")]
	public string Name { get; set; }

	[Option('t', "type", Required = false, HelpText = "Layer type to disambiguate when the name appears in multiple categories. One of: terrain, geometry, plan, result, boundarycondition.")]
	public string TypeArg { get; set; }

	public bool Execute()
	{
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		OperationResult operationResult = Project.TryLoad(ProjectPath, out var prj);
		if (!operationResult)
		{
			Console.WriteLine("Could not load project from " + ProjectPath.InDoubleQuotes() + ": " + operationResult.GetConcatenatedMessages());
			return false;
		}
		CoreFileType? typeFilter = null;
		if (!string.IsNullOrEmpty(TypeArg))
		{
			if (!CliHelpers.TryParseCoreFileType(TypeArg, out var type))
			{
				Console.WriteLine("Unknown --type: " + TypeArg.InDoubleQuotes() + " (expected one of: terrain, geometry, plan, result, boundarycondition).");
				return false;
			}
			typeFilter = type;
		}
		List<(CoreFileType, string, string)> matches = FindMatches(prj, Name, typeFilter);
		if (matches.Count == 0)
		{
			Console.WriteLine("No layer named " + Name.InDoubleQuotes() + " found in project.");
			return false;
		}
		if (matches.Count > 1)
		{
			Console.WriteLine("Multiple layers named " + Name.InDoubleQuotes() + " found - use --type to disambiguate:");
			foreach (var matche in matches)
			{
				Console.WriteLine($"  {matche.Item1}: {matche.Item3}");
			}
			return false;
		}
		(CoreFileType, string, string) tuple = matches[0];
		File.Delete(tuple.Item3);
		Console.WriteLine($"Removed {tuple.Item1}: {tuple.Item3}");
		return true;
	}

	private static List<(CoreFileType type, string name, string filename)> FindMatches(Project project, string name, CoreFileType? typeFilter)
	{
		List<(CoreFileType, string, string)> valueTuples = new List<(CoreFileType, string, string)>();
		if ((!typeFilter.HasValue || typeFilter == CoreFileType.Terrain) ? true : false)
		{
			valueTuples.AddRange(project.Terrains.Select((Terrain x) => (Terrain: CoreFileType.Terrain, Name: x.Name, Filename: x.Filename)));
		}
		if ((!typeFilter.HasValue || typeFilter.GetValueOrDefault() == CoreFileType.Geometry) ? true : false)
		{
			valueTuples.AddRange(project.Geometries.Select((Geometry x) => (Geometry: CoreFileType.Geometry, Name: x.Name, Filename: x.Filename)));
		}
		if ((!typeFilter.HasValue || typeFilter == CoreFileType.Plan) ? true : false)
		{
			valueTuples.AddRange(project.Plans.Select((Plan x) => (Plan: CoreFileType.Plan, Name: x.Name, Filename: x.Filename)));
		}
		if ((!typeFilter.HasValue || typeFilter == CoreFileType.Result) ? true : false)
		{
			valueTuples.AddRange(project.Results.Select((Result x) => (Result: CoreFileType.Result, Name: x.Name, Filename: x.Filename)));
		}
		if ((!typeFilter.HasValue || typeFilter == CoreFileType.BoundaryCondition) ? true : false)
		{
			valueTuples.AddRange(project.BoundaryConditions.Select((BoundaryCondition x) => (BoundaryCondition: CoreFileType.BoundaryCondition, Name: x.Name, Filename: x.Filename)));
		}
		return valueTuples.Where(((CoreFileType, string, string) t) => string.Equals(t.Item2, name, StringComparison.OrdinalIgnoreCase)).ToList();
	}
}
