namespace Geospatial.IO.Network.USGS;

public record TiffFileScanResult(bool Success, long FileId, long Size, string ProjectionWkt, float CellSize, float Left, float Top, float Right, float Bottom);
