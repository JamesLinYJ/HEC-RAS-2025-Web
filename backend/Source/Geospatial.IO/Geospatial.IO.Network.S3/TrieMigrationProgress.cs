namespace Geospatial.IO.Network.S3;

public record TrieMigrationProgress(long RowsProcessed, long UniqueSegments, double RowsPerSecond);
