using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.USGS;

internal class StacSpatial
{
	[JsonPropertyName("bbox")]
	public List<double[]> Bbox { get; set; }
}
