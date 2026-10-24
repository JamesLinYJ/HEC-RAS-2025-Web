using System;
using System.Collections;
using System.Collections.Generic;
using Geospatial.DataStructures;

namespace Geospatial.Vectors;

public class LazyLoadPointCollection : LazyLoadVectorCollection<Point>, IPointCollection, IVectorCollection<Point>, IReadOnlyVectorCollection<Point>, IEnumerable<Point>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Point>, IReadOnlyCollection<Point>, IReadOnlyPointCollection, ISpatiallyIndexedPointCollection
{
	protected override PointCollection BaseCollection => base.BaseCollection as PointCollection;

	public SpatialIndex<int> PointSpatialIndex => BaseCollection.PointSpatialIndex;

	public LazyLoadPointCollection(Func<Memory<Point>> loadElements)
		: base((Func<IVectorCollection<Point>>)(() => new PointCollection(loadElements())))
	{
	}

	public LazyLoadPointCollection(Func<IPointCollection> loadElements)
		: base((Func<IVectorCollection<Point>>)loadElements)
	{
	}
}
