namespace Geospatial.IO.Network.USGS;

public record WESMProject
{
	public string Project { get; init; }

	public string Workunit { get; init; }

	public string SourcedemLink { get; init; }

	public double Resolution { get; init; }

	public string OnemeterCategory { get; init; }
}
