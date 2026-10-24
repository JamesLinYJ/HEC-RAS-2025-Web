using System.Collections;
using System.Collections.Generic;
using Geospatial;
using Geospatial.DataStructures;
using Geospatial.Features;
using Geospatial.Vectors;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class ObservablePolylineFeatureCollection : ObservableFeatureCollection<Polyline>, IPolylineCollection, IVectorCollection<Polyline>, IReadOnlyVectorCollection<Polyline>, IEnumerable<Polyline>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polyline>, IReadOnlyCollection<Polyline>, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	private readonly IPolylineCollection _inner;

	public ObservablePolylineFeatureCollection(IPolylineCollection inner, ObservableTable observableTable)
		: base(new FeatureCollection<Polyline>(inner, observableTable.Table), observableTable)
	{
		_inner = inner;
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return ((ISpatiallyIndexedSegmentCollection)_inner).GetSegmentSpatialIndex();
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _inner.GetSpatialIndex();
	}
}
