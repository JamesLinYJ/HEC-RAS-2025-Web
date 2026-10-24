using System;
using System.IO;
using System.Linq;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Process.Helpers;
using Utility.Extensions;

namespace Ras.Process.TerrainCommands;

[Verb("remove", false, null, HelpText = "Removes a named input (raster or vector) from an existing RAS terrain.")]
public class TerrainRemoveArgs
{
	public const string Verb = "remove";

	[Value(0, MetaName = "terrain", Required = true, HelpText = "Terrain file (*.h5) to modify.")]
	public string TerrainFilename { get; set; }

	[Value(1, MetaName = "name", Required = true, HelpText = "Name of the input (raster or vector) to remove.")]
	public string LayerName { get; set; }

	public bool Execute()
	{
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		if (!File.Exists(TerrainFilename))
		{
			Console.WriteLine(TerrainFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		Terrain terrain = new Terrain(TerrainFilename);
		ITerrainInput terrainInput = terrain.Layers.FirstOrDefault((ITerrainInput input) => input.Name.RoughlyEquals(LayerName));
		if (terrainInput == null)
		{
			Console.WriteLine("No input named " + LayerName.InDoubleQuotes() + " found in terrain.");
			Console.WriteLine("Available inputs:");
			foreach (ITerrainInput layer in terrain.Layers)
			{
				Console.WriteLine($"  {layer.Name} ({layer.TypeName})");
			}
			return false;
		}
		terrain.Remove(terrainInput);
		terrain.Save();
		Console.WriteLine("Removed " + terrainInput.Name + " from " + terrain.Filename);
		return true;
	}
}
