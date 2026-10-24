using System.Collections;
using System.Collections.Generic;
using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public interface IReadOnlyComplexPolygonCollection : IReadOnlyVectorCollection<ComplexPolygon>, IEnumerable<ComplexPolygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<ComplexPolygon>, IReadOnlyCollection<ComplexPolygon>
{
	List<Polygon> FlattenedRingsList { get; }

	SpatialIndex<int> GetSpatialIndex();

	SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex();
}
