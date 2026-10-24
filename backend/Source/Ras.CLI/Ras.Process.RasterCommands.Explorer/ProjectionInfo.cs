namespace Ras.Process.RasterCommands.Explorer;

public record ProjectionInfo(bool HasProjection, string Name, int Epsg, string Units, string Wkt, string ExtraDetail);
