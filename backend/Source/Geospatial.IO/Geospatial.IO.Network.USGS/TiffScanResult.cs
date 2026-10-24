using Geospatial.Rasters;
using Utility.Logging;

namespace Geospatial.IO.Network.USGS;

public class TiffScanResult
{
	public string Url { get; init; }

	public long FileSize { get; init; }

	public OperationResult Result { get; init; }

	public RasterDefinition NativeRaster { get; init; }

	public string ProjectionWkt { get; init; }

	public bool Success => Result?.Result ?? false;
}
