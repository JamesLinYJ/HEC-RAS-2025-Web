using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.TerrainCommands;

[Verb("add", false, null, HelpText = "Adds one or more input rasters to an existing RAS terrain.")]
public class TerrainAddArgs
{
	public const string Verb = "add";

	[Value(0, MetaName = "terrain", Required = true, HelpText = "Existing terrain file (*.h5) to add rasters to.")]
	public string TerrainFilename { get; set; }

	[Option("files", Required = true, HelpText = "Input raster files to add, ordered from high to low priority.")]
	public IEnumerable<string> InputFiles { get; set; }

	[Option('r', "rounding", Required = false, HelpText = "Desired rounding, as an integer base-2 value. 0 := no rounding, default.")]
	public int Rounding { get; set; }

	public bool Execute()
	{
		ProgressReporter pr = ProgressReporter.ConsoleWrite();
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		if (!File.Exists(TerrainFilename))
		{
			Console.WriteLine(TerrainFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (!TerrainOps.AllInputsExist(InputFiles))
		{
			return false;
		}
		Terrain terrain = new Terrain(TerrainFilename);
		Projection projection = terrain.TryGetProjection();
		if (projection == null)
		{
			Console.WriteLine("WARNING: Terrain " + TerrainFilename.InDoubleQuotes() + " has no projection. Rasters will be imported without reprojection.");
		}
		if (!TerrainOps.AddRastersToTerrain(terrain, InputFiles, projection, Rounding, pr))
		{
			return false;
		}
		terrain.Save();
		Console.WriteLine("Saved " + terrain.Filename);
		return true;
	}
}
