using System.Collections;
using System.Collections.Generic;
using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public interface IMeshCollection : IVectorCollection<Mesh>, IReadOnlyVectorCollection<Mesh>, IEnumerable<Mesh>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Mesh>, IReadOnlyCollection<Mesh>
{
	SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex();

	SpatialIndex<int> GetSpatialIndex();
}
