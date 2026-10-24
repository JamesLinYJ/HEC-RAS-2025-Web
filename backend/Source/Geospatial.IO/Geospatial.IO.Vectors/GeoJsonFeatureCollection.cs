using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Vectors;

public class GeoJsonFeatureCollection
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("features")]
	public List<GeoJsonFeature> Features { get; set; }
}
