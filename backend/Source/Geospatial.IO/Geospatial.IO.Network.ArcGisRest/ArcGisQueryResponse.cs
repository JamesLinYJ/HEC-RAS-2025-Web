using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.ArcGisRest;

public class ArcGisQueryResponse
{
	[JsonPropertyName("objectIdFieldName")]
	public string ObjectIdFieldName { get; set; }

	[JsonPropertyName("globalIdFieldName")]
	public string GlobalIdFieldName { get; set; }

	[JsonPropertyName("geometryType")]
	public string GeometryType { get; set; }

	[JsonPropertyName("hasZ")]
	public bool HasZ { get; set; }

	[JsonPropertyName("hasM")]
	public bool HasM { get; set; }

	[JsonPropertyName("spatialReference")]
	public ArcGisSpatialReference SpatialReference { get; set; }

	[JsonPropertyName("fields")]
	public List<ArcGisField> Fields { get; set; }

	[JsonPropertyName("features")]
	public List<ArcGisFeature> Features { get; set; }

	[JsonPropertyName("exceededTransferLimit")]
	public bool ExceededTransferLimit { get; set; }

	[JsonPropertyName("error")]
	public ArcGisError Error { get; set; }
}
