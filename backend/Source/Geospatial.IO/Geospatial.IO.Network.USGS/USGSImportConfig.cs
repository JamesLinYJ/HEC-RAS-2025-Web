using System;
using System.IO;
using Utility.Files;

namespace Geospatial.IO.Network.USGS;

public class USGSImportConfig
{
	public string DownloadDirectory { get; set; }

	public bool DeleteDownloadedFileAfterImport { get; set; }

	public InterpolatedRasterFileConverterConfig<float> RasterImportOptions { get; set; }

	public static USGSImportConfig Default()
	{
		return new USGSImportConfig
		{
			DownloadDirectory = Path.GetTempPath(),
			DeleteDownloadedFileAfterImport = true,
			RasterImportOptions = InterpolatedRasterFileConverterConfig<float>.Default()
		};
	}

	public static USGSImportConfig Preserve(string dlDirectory)
	{
		return new USGSImportConfig
		{
			DownloadDirectory = dlDirectory,
			DeleteDownloadedFileAfterImport = false,
			RasterImportOptions = InterpolatedRasterFileConverterConfig<float>.Default()
		};
	}

	public string GetAvailableDownloadFilenameFromURL(string url)
	{
		if (string.IsNullOrEmpty(DownloadDirectory))
		{
			throw new ArgumentException("DownloadDirectory not set. You may need to construct with Default");
		}
		string literalFilenameFromURL = GetLiteralFilenameFromURL(url);
		return FileHelpers.UniqueFilename(Path.Combine(DownloadDirectory, literalFilenameFromURL));
	}

	public string GetLiteralFilenameFromURL(string url)
	{
		string[] parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			return "error.tif";
		}
		string part = parts[^1];
		if (!part.EndsWith(".tif"))
		{
			part += ".tif";
		}
		return part;
	}
}
