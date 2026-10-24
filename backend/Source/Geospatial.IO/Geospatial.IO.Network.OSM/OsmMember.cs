using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.OSM;

public class OsmMember
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("ref")]
	public long Reference { get; set; }

	[JsonPropertyName("role")]
	public string Role { get; set; }

	[JsonPropertyName("lat")]
	public double? Lat { get; set; }

	[JsonPropertyName("lon")]
	public double? Lon { get; set; }

	[JsonPropertyName("geometry")]
	public List<OsmCoordinate> Geometry { get; set; }
}
