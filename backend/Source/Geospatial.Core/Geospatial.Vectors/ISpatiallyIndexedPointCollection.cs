using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public interface ISpatiallyIndexedPointCollection
{
	SpatialIndex<int> PointSpatialIndex { get; }
}
