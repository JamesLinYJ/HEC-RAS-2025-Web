using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.ArcGisRest;

public class ArcGisFeature
{
	[JsonPropertyName("attributes")]
	public Dictionary<string, object> Attributes { get; set; }

	[JsonPropertyName("geometry")]
	public ArcGisGeometry Geometry { get; set; }
}
