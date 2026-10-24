using System.Text.Json.Serialization;
using Geospatial.IO.Vectors;

namespace Geospatial.IO.Network.NSI;

public class NSIFeature
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("geometry")]
	public GeoJsonGeometry Geometry { get; set; }

	[JsonPropertyName("properties")]
	public NSIStructure Properties { get; set; }
}
