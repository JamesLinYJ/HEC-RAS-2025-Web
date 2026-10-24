using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Ras.Process.Helpers;
using TiffBinaryReader;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.RasterCommands;

[Verb("translate", false, null, HelpText = "Creates a RAS-compliant .tif file from integer or float data sources. File is compressed and pyramided, with optional rounding parameters.Can read any raster our GDAL build recognizes. Will use faster native drivers for 1st-party formats we recognize (.tif).")]
public class TranslateArgs
{
	public const string Verb = "translate";

	[Option('s', "source", Required = true, HelpText = "Input raster file.")]
	public string SourceFile { get; set; }

	[Option('o', "output", Required = true, HelpText = "Output .tif filename. If file already exists, must be combined with --force argument.")]
	public string OutputFilename { get; set; }

	[Option('f', "force", Required = false, HelpText = "Force overwrite existing target file. Defaults to false.")]
	public bool Force { get; set; }

	[Option("use-gdal", Required = false, HelpText = "Force use the GDAL driver stack for reading the raster file.")]
	public bool UseGdalDrivers { get; set; }

	public bool Execute()
	{
		try
		{
			ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
			CliHelpers.TimeStep("Initializing GDAL...", () =>
			{
				GDALSetup.InitializeMultiplatform();
			});
			if (!File.Exists(SourceFile))
			{
				throw new FileNotFoundException(SourceFile);
			}
			if (File.Exists(OutputFilename))
			{
				if (!Force)
				{
					throw new Exception("Output file " + OutputFilename.InDoubleQuotes() + " already exists. If you intend to overwrite this file, rerun with --force.");
				}
				File.Delete(OutputFilename);
				progressReporter.ReportMessage("Output file deleted: " + OutputFilename.InDoubleQuotes());
			}
			Projection projection = GDALRaster.GetProjection(SourceFile);
			bool condition = false;
			if (!UseGdalDrivers)
			{
				OperationResult operationResult = TiffFile.TryLoad(SourceFile, out var file, out var _, out var canReadAllDirectories);
				try
				{
					if (operationResult.Result & canReadAllDirectories)
					{
						condition = true;
						TiffDirectory tiffDirectory = file.Directories[0];
						progressReporter.ReportMessage("Using native TIFF reader.");
						if (tiffDirectory.Metadata.ParsedType == typeof(float))
						{
							using TiffDataSource<float> ftp = new TiffDataSource<float>(file);
							IRasterPyramid<float> rasterPyramid = ftp.AsRasterizer();
							TiffExportEngine.ExportWithOverviews(OutputFilename, rasterPyramid, rasterPyramid.RasterDefinition, projection, progressReporter);
						}
						else if (tiffDirectory.Metadata.ParsedType == typeof(byte))
						{
							using TiffDataSource<byte> ftp2 = new TiffDataSource<byte>(file);
							IRasterPyramid<byte> rasterPyramid2 = ftp2.AsRasterizer();
							TiffExportEngine.ExportWithOverviews(OutputFilename, rasterPyramid2, rasterPyramid2.RasterDefinition, projection, progressReporter);
						}
						else
						{
							if (!(tiffDirectory.Metadata.ParsedType == typeof(int)))
							{
								throw new Exception("File data type not supported: " + tiffDirectory.Metadata.ParsedType);
							}
							using TiffDataSource<int> ftp3 = new TiffDataSource<int>(file);
							IRasterPyramid<int> rasterPyramid3 = ftp3.AsRasterizer();
							TiffExportEngine.ExportWithOverviews(OutputFilename, rasterPyramid3, rasterPyramid3.RasterDefinition, projection, progressReporter);
						}
					}
				}
				catch
				{
					throw;
				}
				finally
				{
					file?.Dispose();
					file = null;
				}
			}
			if (!condition)
			{
				progressReporter.ReportMessage("Using GDAL raster reader.");
				using GDALRaster gDALRaster = new GDALRaster(SourceFile);
				Type type = gDALRaster.BandDataType(1);
				if (type == typeof(float))
				{
					IRaster<float> raster = new GdalBandedRaster<float>(gDALRaster).AsSingleProfile(0);
					TiffExportEngine.ExportWithOverviews(OutputFilename, raster, raster.RasterDefinition, projection, progressReporter);
				}
				else
				{
					if (!(type == typeof(int)))
					{
						throw new Exception("File data type not supported: " + type);
					}
					IRaster<int> raster2 = new GdalBandedRaster<int>(gDALRaster).AsSingleProfile(0);
					TiffExportEngine.ExportWithOverviews(OutputFilename, raster2, raster2.RasterDefinition, projection, progressReporter);
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}
}
