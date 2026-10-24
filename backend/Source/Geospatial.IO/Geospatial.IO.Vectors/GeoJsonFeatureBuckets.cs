using Geospatial.Features;

namespace Geospatial.IO.Vectors;

public sealed class GeoJsonFeatureBuckets
{
	public PointCollectionFeatureCollection Points { get; init; }

	public PolylineCollectionFeatureCollection Polylines { get; init; }

	public ComplexPolygonCollectionFeatureCollection Polygons { get; init; }
}
