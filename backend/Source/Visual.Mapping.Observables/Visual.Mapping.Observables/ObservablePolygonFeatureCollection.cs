using System.Collections;
using System.Collections.Generic;
using Geospatial;
using Geospatial.DataStructures;
using Geospatial.Features;
using Geospatial.Vectors;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class ObservablePolygonFeatureCollection : ObservableFeatureCollection<Polygon>, IPolygonCollection, IVectorCollection<Polygon>, IReadOnlyVectorCollection<Polygon>, IEnumerable<Polygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polygon>, IReadOnlyCollection<Polygon>, IReadOnlyPolygonCollection, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	private readonly IPolygonCollection _inner;

	public ObservablePolygonFeatureCollection(IPolygonCollection inner, ObservableTable observableTable)
		: base(new FeatureCollection<Polygon>(inner, observableTable.Table), observableTable)
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
