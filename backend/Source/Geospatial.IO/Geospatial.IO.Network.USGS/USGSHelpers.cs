using System;
using System.IO;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;
using Utility.Units;
using Utility.Web;

namespace Geospatial.IO.Network.USGS;

public static class USGSHelpers
{
	public const UnitSystem FallbackUnitSystem = UnitSystem.SI;

	private static readonly ILogger _logger = LogHelper.GetLogger("USGSHelpers");

	public static async Task<USGSDownloadResult> TryDownloadAndParseUSGSTileAsync(string url, string outputFilename, USGSImportConfig config, ProgressReporter pr = null)
	{
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		ProgressReporter reporter = pr.WithProgressModifier(0, 0.5f);
		bool downloadWithProgress = true;
		string downloadedFile = config.GetAvailableDownloadFilenameFromURL(url);
		try
		{
			using FileStream fs = new FileStream(downloadedFile, FileMode.OpenOrCreate);
			downloadWithProgress = await WebHelper.DownloadWithProgress(SharedWebResourceManager.Client, url, fs, reporter);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error downloading USGS file.");
			downloadWithProgress = false;
		}
		if (!downloadWithProgress)
		{
			FileHelpers.TryDelete(downloadedFile);
			pr.ReportMessage("Error downloading file.");
			return new USGSDownloadResult
			{
				Result = false
			};
		}
		USGSDownloadResult retn = new USGSDownloadResult();
		ProgressReporter prImporter = pr.WithProgressModifier(50, 0.5f);
		Projection sourcePrj = null;
		using (GDALRaster gDALRaster = new GDALRaster(downloadedFile))
		{
			sourcePrj = gDALRaster.GetProjection();
			gDALRaster.GetNativeRaster();
		}
		if (!config.RasterImportOptions.ConvertScaleFactor.HasValue)
		{
			double lengthScaleFactor = Projection.GetLengthScaleFactor(sourcePrj, config.RasterImportOptions.DestinationProjection, UnitSystem.SI, UnitSystem.SI);
			config.RasterImportOptions.ConvertScaleFactor = (float)lengthScaleFactor;
		}
		retn.ScaleFactorUsed = config.RasterImportOptions.ConvertScaleFactor ?? 1f;
		OperationResult operationResult = await Task.Run(() => TiffExportEngine.ConvertFile(downloadedFile, outputFilename, config.RasterImportOptions, prImporter));
		if (config.DeleteDownloadedFileAfterImport)
		{
			FileHelpers.TryDelete(downloadedFile);
		}
		retn.Result = operationResult;
		if (operationResult.Result)
		{
			retn.ConvertedFile = outputFilename;
			if (!config.DeleteDownloadedFileAfterImport)
			{
				retn.DownloadedFile = downloadedFile;
			}
		}
		return retn;
	}

	public static string FormatBytesAsString(long bytes)
	{
		if (bytes < 1000000000)
		{
			return $"{(double)bytes / 1000000.0:F2} MB";
		}
		return $"{(double)bytes / 1000000000.0:F2} GB";
	}
}
