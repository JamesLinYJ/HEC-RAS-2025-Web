namespace Geospatial.IO.Network.S3;

public record S3IndexProgress(long ObjectsEnumerated, long ObjectsWritten, string CurrentKey);
