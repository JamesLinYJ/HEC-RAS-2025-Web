using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.OSM;

public class OsmCoordinate
{
	[JsonPropertyName("lat")]
	public double Lat { get; set; }

	[JsonPropertyName("lon")]
	public double Lon { get; set; }
}
