using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.USGS;

internal class StacCollection
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("extent")]
	public StacExtent Extent { get; set; }
}
