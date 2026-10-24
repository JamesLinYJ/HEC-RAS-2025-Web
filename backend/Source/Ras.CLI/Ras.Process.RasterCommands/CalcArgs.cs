using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using CommandLine.Text;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Ras.Process.Helpers;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.RasterCommands;

[Verb("calc", false, null, HelpText = "Combines raster files using a mathematical expression. Input rasters are referenced as r1, r2, r3, etc. in the equation. The NoData value can be referenced with the variable 'ndv'.")]
public class CalcArgs
{
	public const string Verb = "calc";

	[Option('i', "input", Required = true, Min = 1, HelpText = "Input raster file(s). Can be specified multiple times. Referenced as r1, r2, r3, etc. in the equation.")]
	public IEnumerable<string> InputFiles { get; set; }

	[Option('o', "output", Required = true, HelpText = "Output filename. If file already exists, must be combined with --force argument.")]
	public string OutputFilename { get; set; }

	[Option('e', "equation", Required = true, HelpText = "Mathematical expression combining the rasters. Uses r1, r2, r3, etc. for inputs. Supports: +, -, *, /, ^, sqrt, abs, min, max, sin, cos, tan, log, pow, lerp, etc. Examples: '(r1 + r2) / 2' (average), 'max(r1, r2)', 'r1 - r2' (difference).")]
	public string Equation { get; set; }

	[Option('f', "force", Required = false, HelpText = "Force overwrite existing target file. Defaults to false.")]
	public bool Force { get; set; }

	[Option("ndv-behavior", Required = false, Default = CalculatorNdvBehavior.Allow, HelpText = "How to handle NoData values in the output. Allow: NoData propagates through calculations. WriteNDV: Write NoData as the output when any input is NoData. WriteZero: Write zero as the output when any input is NoData.")]
	public CalculatorNdvBehavior NoDataBehavior { get; set; }

	[Usage(ApplicationAlias = "ras")]
	public static IEnumerable<Example> Examples => new Example[1]
	{
		new Example("Subtract Depth (with NoData checks)", new CalcArgs
		{
			InputFiles = new string[2] { "data/input1.tif", "data/input2.tif" },
			OutputFilename = "data/output.tif",
			Equation = "if(r1 == ndv and r2 == ndv, ndv, if(r1 == ndv, 0, r1) - if(r2 == ndv, 0, r2))"
		})
	};

	public bool Execute()
	{
		try
		{
			ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
			List<string> inputFiles = InputFiles.ToList();
			if (inputFiles.Count == 0)
			{
				throw new Exception("At least one input file is required.");
			}
			CliHelpers.TimeStep("Initializing GDAL...", () =>
			{
				GDALSetup.InitializeMultiplatform();
			});
			for (int intervalIndex = 0; intervalIndex < inputFiles.Count; intervalIndex++)
			{
				if (!File.Exists(inputFiles[intervalIndex]))
				{
					throw new FileNotFoundException($"Input file r{intervalIndex + 1} not found: " + inputFiles[intervalIndex]);
				}
			}
			if (!CliHelpers.CheckDeleteOutputFile(OutputFilename, Force))
			{
				return false;
			}
			progressReporter.ReportMessage("Equation: " + Equation);
			progressReporter.ReportMessage($"Input count: {inputFiles.Count}");
			progressReporter.ReportMessage("NoData Behavior: " + NoDataBehavior);
			Projection projection = GDALRaster.GetProjection(inputFiles[0]);
			List<IDisposable> iDisposables = new List<IDisposable>();
			List<RasterCalculatorInput> rasterCalculatorInputs = new List<RasterCalculatorInput>();
			IRaster<float> raster = null;
			try
			{
				for (int intervalIndex2 = 0; intervalIndex2 < inputFiles.Count; intervalIndex2++)
				{
					string message = $"r{intervalIndex2 + 1}";
					progressReporter.ReportMessage("Loading " + message + ": " + inputFiles[intervalIndex2]);
					IRaster<float> raster2 = RasterReader.TryGetBestRasterizer<float>(inputFiles[intervalIndex2], out var _, out var cleanup);
					if (raster2 == null)
					{
						throw new Exception("Failed to load " + message + ": " + inputFiles[intervalIndex2]);
					}
					if (cleanup != null)
					{
						iDisposables.Add(cleanup);
					}
					if (raster == null)
					{
						raster = raster2;
					}
					rasterCalculatorInputs.Add(RasterCalculatorInput.FromStatic(message, raster2));
				}
				progressReporter.ReportMessage("Compiling expression...");
				Dictionary<string, float> constants = new Dictionary<string, float> { { "ndv", -9999f } };
				OperationResult operationResult = RasterCalculator.TryCreate(Equation, rasterCalculatorInputs, constants, out var calculator);
				if (!operationResult.Result)
				{
					throw new Exception("Failed to compile expression: " + operationResult.GetConcatenatedMessages());
				}
				calculator.NoDataBehavior = NoDataBehavior;
				IResample<float> resampler = calculator.AsStaticResampler();
				RasterDefinition exportRdef = new RasterDefinition(calculator.ValidExtent, raster.RasterDefinition.CellSize);
				progressReporter.ReportMessage($"Output extent: {calculator.ValidExtent}");
				progressReporter.ReportMessage($"Output size: {exportRdef.Rows} rows x {exportRdef.Cols} cols");
				TiffExportEngine.ExportWithOverviews(OutputFilename, resampler, exportRdef, projection, progressReporter);
				progressReporter.ReportMessage("Complete: " + OutputFilename);
				return true;
			}
			finally
			{
				foreach (IDisposable iDisposable in iDisposables)
				{
					iDisposable?.Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}
}
