using System;
using System.Threading.Tasks;
using CommandLine;
using Geospatial.IO;
using Geospatial.Rasters;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Progress;

namespace Ras.Process;

[Verb("extract-terrain", false, null, HelpText = "Extracts a low-res terrain from a result or geometry.")]
public class ExtractTerrainArgs
{
	public const string Verb = "extract-terrain";

	[Option('s', "source", Required = true, HelpText = "Source geometry or result filename. Must have property tables embedded.")]
	public string Source { get; set; }

	[Option('o', "output", Required = true, HelpText = "Destination TIFF filename. Should not exist. E.G. \"./TerrainOut.tif\"")]
	public string OutputFilename { get; set; }

	[Option('c', "cell-size", Required = false, HelpText = "Cell-size for the tiff. If unset, will use a reasonable value considering mesh cell size.")]
	public float CellSize { get; set; }

	[Option('f', "force", Required = false, HelpText = "Will delete an existing file of the same name. Defaults to false.")]
	public bool Force { get; set; }

	public Task<bool> ExecuteAsync()
	{
		return Task.FromResult(Execute());
	}

	public bool Execute()
	{
		if (!CliHelpers.CheckDeleteOutputFile(OutputFilename, Force))
		{
			return false;
		}
		if (!CliHelpers.CheckInitializeGDAL())
		{
			return false;
		}
		Geometry geometry = new Geometry(Source);
		IResample<float> resample = geometry.ApproximateTerrainFromTables();
		double cellSize = Math.Sqrt(geometry.Mesh.GetMeshStatistics().CellAverageArea) / 10.0;
		if (CellSize != 0f)
		{
			cellSize = CellSize;
		}
		geometry.LoadProjectMetadataFromH5Header(out var _, out var projection, out var _);
		InterpolatedRasterFileConverterConfig<float>.Default().DestinationProjection = projection;
		TiffExportEngine.ExportWithOverviews(OutputFilename, resample, new RasterDefinition(resample.ValidExtent, cellSize), projection, ProgressReporter.ConsoleWrite());
		return true;
	}
}
