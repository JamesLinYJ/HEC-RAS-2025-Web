using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.OSM;

public class OsmBounds
{
	[JsonPropertyName("minlat")]
	public double MinLat { get; set; }

	[JsonPropertyName("minlon")]
	public double MinLon { get; set; }

	[JsonPropertyName("maxlat")]
	public double MaxLat { get; set; }

	[JsonPropertyName("maxlon")]
	public double MaxLon { get; set; }
}
