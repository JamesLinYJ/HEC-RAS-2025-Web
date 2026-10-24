using Utility.Logging;

namespace Geospatial.IO.Network.USGS;

public class USGSDownloadResult
{
	public OperationResult Result;

	public string DownloadedFile;

	public string ConvertedFile;

	public double ScaleFactorUsed;
}
