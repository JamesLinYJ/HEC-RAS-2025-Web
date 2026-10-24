using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.USGS;

internal class StacAsset
{
	[JsonPropertyName("href")]
	public string Href { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }
}
