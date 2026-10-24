using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.RasServer;

public record ProjectionInfo([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("wkt")] string Wkt, [property: JsonPropertyName("units")] string Units);
