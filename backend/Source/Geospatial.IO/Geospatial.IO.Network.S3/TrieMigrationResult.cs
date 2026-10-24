using System;

namespace Geospatial.IO.Network.S3;

public record TrieMigrationResult(long TotalRows, long UniquePathSegments, TimeSpan ElapsedTime, long SourceSizeBytes, long DestSizeBytes)
{
	public double CompressionRatio => 1.0 - (double)DestSizeBytes / (double)SourceSizeBytes;
}
