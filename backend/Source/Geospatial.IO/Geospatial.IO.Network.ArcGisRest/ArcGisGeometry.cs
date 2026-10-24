using System.Collections.Generic;
using System.Text.Json.Serialization;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.ArcGisRest;

public class ArcGisGeometry
{
	[JsonPropertyName("x")]
	public double? X { get; set; }

	[JsonPropertyName("y")]
	public double? Y { get; set; }

	[JsonPropertyName("z")]
	public double? Z { get; set; }

	[JsonPropertyName("paths")]
	public List<List<Point3D>> Paths { get; set; }

	[JsonPropertyName("rings")]
	public List<List<Point3D>> Rings { get; set; }

	[JsonPropertyName("spatialReference")]
	public ArcGisSpatialReference SpatialReference { get; set; }

	public bool IsPoint
	{
		get
		{
			if (X.HasValue)
			{
				return Y.HasValue;
			}
			return false;
		}
	}

	public bool IsPolyline
	{
		get
		{
			if (Paths != null)
			{
				return Paths.Count > 0;
			}
			return false;
		}
	}

	public bool IsPolygon
	{
		get
		{
			if (Rings != null)
			{
				return Rings.Count > 0;
			}
			return false;
		}
	}
}
