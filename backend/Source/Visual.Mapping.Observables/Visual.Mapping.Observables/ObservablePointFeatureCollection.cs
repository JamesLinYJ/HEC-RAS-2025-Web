using System.Collections;
using System.Collections.Generic;
using Geospatial;
using Geospatial.DataStructures;
using Geospatial.Features;
using Geospatial.Vectors;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class ObservablePointFeatureCollection : ObservableFeatureCollection<Point>, IPointCollection, IVectorCollection<Point>, IReadOnlyVectorCollection<Point>, IEnumerable<Point>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Point>, IReadOnlyCollection<Point>, IReadOnlyPointCollection, ISpatiallyIndexedPointCollection
{
	public PointCollection InnerPoints { get; }

	public SpatialIndex<int> PointSpatialIndex => InnerPoints.PointSpatialIndex;

	public ObservablePointFeatureCollection(PointCollection inner, ObservableTable observableTable)
		: base(new FeatureCollection<Point>(inner, observableTable.Table), observableTable)
	{
		InnerPoints = inner;
	}
}
