using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.USGS;

internal class StacProperties
{
	[JsonPropertyName("datetime")]
	public string Datetime { get; set; }
}
