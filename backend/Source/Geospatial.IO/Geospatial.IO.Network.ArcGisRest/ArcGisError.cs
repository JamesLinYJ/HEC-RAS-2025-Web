using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.ArcGisRest;

public class ArcGisError
{
	[JsonPropertyName("code")]
	public int Code { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }
}
