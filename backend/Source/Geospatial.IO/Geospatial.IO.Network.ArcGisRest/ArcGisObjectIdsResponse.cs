using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.ArcGisRest;

public class ArcGisObjectIdsResponse
{
	[JsonPropertyName("objectIdFieldName")]
	public string ObjectIdFieldName { get; set; }

	[JsonPropertyName("objectIds")]
	public List<long> ObjectIds { get; set; }

	[JsonPropertyName("error")]
	public ArcGisError Error { get; set; }
}
