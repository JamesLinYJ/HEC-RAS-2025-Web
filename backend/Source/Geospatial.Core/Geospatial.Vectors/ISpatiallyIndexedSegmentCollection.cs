using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public interface ISpatiallyIndexedSegmentCollection
{
	SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex();

	SpatialIndex<int> GetSpatialIndex();
}
