namespace Geospatial.IO.Network.USGS;

public record S3TileInfo
{
	public string Url { get; init; }

	public long Size { get; init; }
}
