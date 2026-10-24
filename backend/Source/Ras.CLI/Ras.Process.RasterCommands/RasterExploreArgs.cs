using System;
using System.IO;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Process.RasterCommands.Explorer;

namespace Ras.Process.RasterCommands;

[Verb("explore", false, null, HelpText = "Interactive raster explorer: browse sub-datasets, metadata, projection, and pyramid levels, with basic metadata editing. Use --driver to choose the reading/editing stack.")]
public class RasterExploreArgs
{
	public const string Verb = "explore";

	[Value(0, Required = true, HelpText = "Raster file to explore (e.g. a GeoTIFF).")]
	public string FilePath { get; set; }

	[Option('d', "driver", Default = RasterDriver.Default, HelpText = "Reading/editing stack: 'native' (our TiffBinaryReader, GeoTIFF only), 'gdal' (any GDAL format), or 'default' (native for a plain TIFF we parse, otherwise gdal).")]
	public RasterDriver Driver { get; set; }

	public bool Execute()
	{
		if (!File.Exists(FilePath))
		{
			Console.WriteLine("File not found: " + FilePath);
			return false;
		}
		try
		{
			GDALSetup.InitializeMultiplatform();
			if (!RasterSourceFactory.TryCreate(FilePath, Driver, out var source, out var error))
			{
				Console.WriteLine("Could not open raster: " + error);
				return false;
			}
			return new RasterExplorer(source).Run();
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}
}
