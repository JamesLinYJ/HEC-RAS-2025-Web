namespace Geospatial.IO.Network.USGS;

public record TiffBatchScanConfig(string TrieDbPath, int BatchSize = 1000, int MaxConcurrency = 100);
