using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Process.Helpers;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process;

[Verb("tiff", false, null, HelpText = "Generates a TIFF file from an equation.")]
public class TiffArgs
{
	public const string Verb = "tiff";

	[Option('e', "equation", Required = true, HelpText = "Equation to emit pixel values. Uses (x,y) real-world coordinates. Ex: \"x*sqrt(y)\"")]
	public string Equation { get; set; }

	[Option('o', "output", Required = true, HelpText = "Destination tiff filename. Should not exist. E.G. \"./TerrainOut.tif\"")]
	public string OutputFilename { get; set; }

	[Option('j', "projection", Required = false, HelpText = "Output projection. Can be a filename (interpreted as an ESRI .prj) or EPSG code provided as a string, e.g. 'EPSG:900913'.")]
	public string OutputProjection { get; set; }

	[Option('r', "rounding", Required = false, HelpText = "Desired rounding, as an integer base-2 value. 0 := no rounding, default. (1:=.5, 2:=.25, 3:=.125...)")]
	public int Rounding { get; set; }

	[Option('w', "width", Required = false, HelpText = "Pixels wide for the tiff. Defaults to 100.")]
	public int PixelsWide { get; set; } = 100;

	[Option('h', "height", Required = false, HelpText = "Pixels tall for the tiff. Defaults to 100.")]
	public int PixelsTall { get; set; } = 100;

	[Option('c', "cell-size", Required = false, HelpText = "Cell-size for the tiff. Defaults to 1.")]
	public float CellSize { get; set; } = 1f;

	[Option('f', "force", Required = false, HelpText = "Will delete an existing file of the same name. Defaults to false.")]
	public bool Force { get; set; }

	private RasterDefinition GetRasterDef()
	{
		return new RasterDefinition(new Extent(0.0, (float)PixelsWide * CellSize, 0.0, (float)PixelsTall * CellSize), CellSize);
	}

	public Task<bool> ExecuteAsync()
	{
		return Task.FromResult(Execute());
	}

	public bool Execute()
	{
		ProgressReporter pr = ProgressReporter.ConsoleWrite();
		if (!CliHelpers.CheckDeleteOutputFile(OutputFilename, Force))
		{
			return false;
		}
		if (!CliHelpers.CheckInitializeGDAL())
		{
			return false;
		}
		ExpressionCompiler<float> expressionCompiler = new ExpressionCompiler<float>(Equation, new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "x", "y" }));
		Func<float, float, float> del = expressionCompiler.CompileLambda() as Func<float, float, float>;
		Projection projectionMetadata = null;
		if (!string.IsNullOrEmpty(OutputProjection))
		{
			if (OutputProjection.RoughlyStartsWith("EPSG:"))
			{
				if (!int.TryParse(OutputProjection.Substring("EPSG:".Length), out var result))
				{
					Console.WriteLine("Could not parse EPSG code: " + OutputProjection.InDoubleQuotes());
					return false;
				}
				projectionMetadata = new EPSGProjection(result);
			}
			else
			{
				if (!File.Exists(OutputProjection))
				{
					Console.WriteLine("Expected ESRI .prj file does not exist: " + OutputProjection.InDoubleQuotes());
					return false;
				}
				projectionMetadata = new ESRIProjection(OutputProjection);
			}
		}
		Path.GetDirectoryName(OutputFilename);
		Path.GetFileNameWithoutExtension(OutputFilename);
		RasterDefinition rasterDef = GetRasterDef();
		FunctionalResampler<float> resampler = new FunctionalResampler<float>(rasterDef.Extent, -9999f, (Point pt) => del((float)pt.X, (float)pt.Y));
		Stopwatch stopwatch = Stopwatch.StartNew();
		TiffExportEngine.ExportWithOverviews(OutputFilename, resampler, rasterDef, projectionMetadata, pr);
		stopwatch.Stop();
		Console.WriteLine("Wrote " + OutputFilename + " in " + stopwatch.Elapsed);
		return true;
	}
}
