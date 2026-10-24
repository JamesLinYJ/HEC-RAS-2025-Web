namespace Geospatial.IO.Network.USGS;

public record TiffScanBatchProgress(long TotalRemaining, long TotalProcessed, int BatchSuccessCount, int BatchTotalCount, int RecordsPerSecond, TiffFileScanResult SampleResult);
