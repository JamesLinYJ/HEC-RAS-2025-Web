using System.Text.Json.Serialization;

namespace Ras.Layers;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectionType
{
	[JsonStringEnumMemberName("Unknown")]
	Unknown,
	[JsonStringEnumMemberName("Local")]
	Local,
	[JsonStringEnumMemberName("EPSG")]
	EPSG,
	[JsonStringEnumMemberName("PROJ")]
	Proj,
	[JsonStringEnumMemberName("ESRI")]
	ESRI,
	[JsonStringEnumMemberName("WKT")]
	WKT
}
