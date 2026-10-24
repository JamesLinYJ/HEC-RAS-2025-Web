using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.USGS;

internal class StacExtent
{
	[JsonPropertyName("spatial")]
	public StacSpatial Spatial { get; set; }
}
