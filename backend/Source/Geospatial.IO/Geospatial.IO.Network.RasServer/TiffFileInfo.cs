using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.RasServer;

public record TiffFileInfo([property: JsonPropertyName("fileId")] int FileId, [property: JsonPropertyName("filename")] string Filename, [property: JsonPropertyName("fullPath")] string FullPath, [property: JsonPropertyName("downloadUrl")] string DownloadUrl, [property: JsonPropertyName("size")] long Size, [property: JsonPropertyName("cellSize")] float CellSize, [property: JsonPropertyName("extent")] ExtentDto Extent, [property: JsonPropertyName("projectionId")] long? ProjectionId);
