using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.ArcGisRest;

public class ArcGisSpatialReference
{
	[JsonPropertyName("wkid")]
	public int Wkid { get; set; }

	[JsonPropertyName("latestWkid")]
	public int LatestWkid { get; set; }
}
