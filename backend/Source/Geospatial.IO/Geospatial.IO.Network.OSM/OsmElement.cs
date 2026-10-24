using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.OSM;

public class OsmElement
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("id")]
	public long Id { get; set; }

	[JsonPropertyName("lat")]
	public double? Lat { get; set; }

	[JsonPropertyName("lon")]
	public double? Lon { get; set; }

	[JsonPropertyName("tags")]
	public Dictionary<string, string> Tags { get; set; }

	[JsonPropertyName("geometry")]
	public List<OsmCoordinate> Geometry { get; set; }

	[JsonPropertyName("members")]
	public List<OsmMember> Members { get; set; }

	[JsonPropertyName("bounds")]
	public OsmBounds Bounds { get; set; }

	public bool IsNode => Type == "node";

	public bool IsWay => Type == "way";

	public bool IsRelation => Type == "relation";

	public bool TryGetTag(string key, out string stringValue)
	{
		stringValue = null;
		if (Tags == null)
		{
			return false;
		}
		return Tags.TryGetValue(key, out stringValue);
	}
}
