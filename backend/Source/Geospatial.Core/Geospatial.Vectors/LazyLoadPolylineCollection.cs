using System;
using System.Collections;
using System.Collections.Generic;
using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public class LazyLoadPolylineCollection : LazyLoadVectorCollection<Polyline>, IPolylineCollection, IVectorCollection<Polyline>, IReadOnlyVectorCollection<Polyline>, IEnumerable<Polyline>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polyline>, IReadOnlyCollection<Polyline>, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	protected override PolylineCollection BaseCollection => base.BaseCollection as PolylineCollection;

	public LazyLoadPolylineCollection(Func<IList<Polyline>> loadElements)
		: base((Func<IVectorCollection<Polyline>>)(() => new PolylineCollection(loadElements())))
	{
	}

	public LazyLoadPolylineCollection(Func<IPolylineCollection> loadElements)
		: base((Func<IVectorCollection<Polyline>>)loadElements)
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
