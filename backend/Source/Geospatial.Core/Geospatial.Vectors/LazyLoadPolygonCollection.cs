using System;
using System.Collections;
using System.Collections.Generic;
using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public class LazyLoadPolygonCollection : LazyLoadVectorCollection<Polygon>, IPolygonCollection, IVectorCollection<Polygon>, IReadOnlyVectorCollection<Polygon>, IEnumerable<Polygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polygon>, IReadOnlyCollection<Polygon>, IReadOnlyPolygonCollection, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	protected override PolygonCollection BaseCollection => base.BaseCollection as PolygonCollection;

	public LazyLoadPolygonCollection(Func<IList<Polygon>> loadElements)
		: base((Func<IVectorCollection<Polygon>>)(() => new PolygonCollection(loadElements())))
	{
	}

	public LazyLoadPolygonCollection(Func<IPolygonCollection> loadElements)
		: base((Func<IVectorCollection<Polygon>>)loadElements)
	{
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return BaseCollection.GetSegmentSpatialIndex();
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return BaseCollection.GetSpatialIndex();
	}
}
