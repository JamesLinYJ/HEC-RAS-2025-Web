using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.RasServer;

public record ExtentDto([property: JsonPropertyName("left")] double Left, [property: JsonPropertyName("bottom")] double Bottom, [property: JsonPropertyName("right")] double Right, [property: JsonPropertyName("top")] double Top);
