using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.RasServer;

public record IndexStats([property: JsonPropertyName("totalFiles")] int TotalFiles, [property: JsonPropertyName("indexedFiles")] int IndexedFiles, [property: JsonPropertyName("uniqueProjections")] int UniqueProjections, [property: JsonPropertyName("totalSizeBytes")] long TotalSizeBytes);
