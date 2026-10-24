namespace Geospatial.IO.Network.USGS;

public readonly struct WESMTileCandidate(string url, long size, string project, double resolutionMeters)
{
	public string Url { get; } = url;

	public long Size { get; } = size;

	public string Project { get; } = project;

	public double ResolutionMeters { get; } = resolutionMeters;
}
