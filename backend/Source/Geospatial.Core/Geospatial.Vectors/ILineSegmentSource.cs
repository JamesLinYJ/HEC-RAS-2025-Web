using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public interface ILineSegmentSource
{
	SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex();

	Polyline GetLine(int fid);
}
