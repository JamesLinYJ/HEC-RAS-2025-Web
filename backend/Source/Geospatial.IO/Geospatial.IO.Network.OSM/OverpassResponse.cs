using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.OSM;

public class OverpassResponse
{
	[JsonPropertyName("version")]
	public double Version { get; set; }

	[JsonPropertyName("generator")]
	public string Generator { get; set; }

	[JsonPropertyName("elements")]
	public List<OsmElement> Elements { get; set; }

	[JsonPropertyName("remark")]
	public string Remark { get; set; }
}
