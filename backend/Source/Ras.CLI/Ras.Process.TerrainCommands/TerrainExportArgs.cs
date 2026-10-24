using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.TerrainCommands;

[Verb("export", false, null, HelpText = "Exports a terrain to a new location. Optionally clips to an extent and resamples to a target cell size.")]
public class TerrainExportArgs
{
	public const string Verb = "export";

	[Value(0, MetaName = "terrain", Required = true, HelpText = "Source terrain file (*.h5).")]
	public string TerrainFilename { get; set; }

	[Value(1, MetaName = "output", Required = true, HelpText = "Destination terrain filename (*.h5). Must not already exist.")]
	public string OutputFilename { get; set; }

	[Option("clip", Required = false, HelpText = "Optional clip extent as 'minX,maxX,minY,maxY'. If omitted, exports a full copy.")]
	public string ClipExtent { get; set; }

	[Option("cellsize", Required = false, HelpText = "Optional resample cell size when clipping. Ignored without --clip.")]
	public float CellSize { get; set; }

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
		if (File.Exists(OutputFilename))
		{
			Console.WriteLine(OutputFilename.InDoubleQuotes() + " already exists.");
			return false;
		}
		Terrain terrain = new Terrain(TerrainFilename);
		if (string.IsNullOrEmpty(ClipExtent))
		{
			terrain.ExportFullCopy(OutputFilename);
			Console.WriteLine("Exported full copy to " + OutputFilename);
			return true;
		}
		if (!TryParseExtent(ClipExtent, out var ext))
		{
			Console.WriteLine("Could not parse extent " + ClipExtent.InDoubleQuotes() + ". Expected 'minX,maxX,minY,maxY'.");
			return false;
		}
		terrain.ExportClipped(OutputFilename, ext, CellSize, pr);
		Console.WriteLine("Exported clipped terrain to " + OutputFilename);
		return true;
	}

	private static bool TryParseExtent(string raw, out Extent ext)
	{
		ext = default;
		string[] parts = raw.Split(',');
		if (parts.Length != 4)
		{
			return false;
		}
		if (!double.TryParse(parts[0], out var result) || !double.TryParse(parts[1], out var result2) || !double.TryParse(parts[2], out var result3) || !double.TryParse(parts[3], out var result4))
		{
			return false;
		}
		ext = new Extent(result, result2, result3, result4);
		return true;
	}
}
