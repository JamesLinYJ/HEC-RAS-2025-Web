using System.Text.Json.Serialization;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.USGS;

public class BoundingBoxJson
{
	[JsonPropertyName("minX")]
	public double? MinX { get; set; }

	[JsonPropertyName("maxX")]
	public double? MaxX { get; set; }

	[JsonPropertyName("minY")]
	public double? MinY { get; set; }

	[JsonPropertyName("maxY")]
	public double? MaxY { get; set; }

	public Extent AsExtent()
	{
		return new Extent(MinX ?? 0.0, MaxX ?? 0.0, MinY ?? 0.0, MaxY ?? 0.0);
	}
}
