using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.USGS;

internal class StacItemCollection
{
	[JsonPropertyName("features")]
	public List<StacFeature> Features { get; set; }
}
