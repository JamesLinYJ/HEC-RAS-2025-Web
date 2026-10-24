namespace Geospatial.IO.Network.S3;

public record S3IndexConfig(string ServiceUrl, string BucketName, string OutputDatabasePath, string KeyPrefix = "", int BatchSize = 1000, int ProgressInterval = 10000, int MaxKeysPerRequest = 1000);
