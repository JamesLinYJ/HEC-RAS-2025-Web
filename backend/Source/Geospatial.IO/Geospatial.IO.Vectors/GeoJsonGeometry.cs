using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Vectors;

public class GeoJsonGeometry
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("coordinates")]
	public JsonElement Coordinates { get; set; }
}
