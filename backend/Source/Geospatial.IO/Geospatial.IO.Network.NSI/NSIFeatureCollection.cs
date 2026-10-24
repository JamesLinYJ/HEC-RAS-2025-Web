using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.NSI;

public class NSIFeatureCollection
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("features")]
	public List<NSIFeature> Features { get; set; }
}
