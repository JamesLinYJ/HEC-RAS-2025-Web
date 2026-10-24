using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Vectors;

public class GeoJsonFeature
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("geometry")]
	public GeoJsonGeometry Geometry { get; set; }

	[JsonPropertyName("properties")]
	public JsonElement Properties { get; set; }
}
