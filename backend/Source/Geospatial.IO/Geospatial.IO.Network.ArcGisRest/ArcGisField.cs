using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.ArcGisRest;

public class ArcGisField
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("alias")]
	public string Alias { get; set; }

	[JsonPropertyName("length")]
	public int? Length { get; set; }
}
