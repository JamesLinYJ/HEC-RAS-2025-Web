using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Vectors;

namespace Ras.Layers;

public static class FeatureLayerExtensions
{
	public static SpatialIndex<Tuple<int, int>> ComputeSegmentSpatialIndex<T>(this IReadOnlyList<T> polylineCollection) where T : IReadOnlyList<Segment>
	{
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(Extent.Union(polylineCollection.Select((T pl) => Extent.Union(pl))));
		for (int intervalIndex = 0; intervalIndex < polylineCollection.Count; intervalIndex++)
		{
			T polylineCollection2 = polylineCollection[intervalIndex];
			for (int intervalIndex2 = 0; intervalIndex2 < polylineCollection2.Count; intervalIndex2++)
			{
				spatialIndex.Add(polylineCollection2[intervalIndex2], new Tuple<int, int>(intervalIndex, intervalIndex2));
			}
		}
		return spatialIndex;
	}

	public static SpatialIndex<Tuple<int, int>> ComputePointSpatialIndex<T>(this IReadOnlyList<T> multiPointCollection) where T : IReadOnlyList<Point>
	{
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(Extent.Union(multiPointCollection.Select((T pl) => Extent.Union(pl))));
		for (int intervalIndex = 0; intervalIndex < multiPointCollection.Count; intervalIndex++)
		{
			T multiPointCollection2 = multiPointCollection[intervalIndex];
			for (int intervalIndex2 = 0; intervalIndex2 < multiPointCollection2.Count; intervalIndex2++)
			{
				spatialIndex.Add(multiPointCollection2[intervalIndex2], new Tuple<int, int>(intervalIndex, intervalIndex2));
			}
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> ComputeAreaSpatialIndex(this IReadOnlyList<Polygon> polygonCollection)
	{
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent.Union(polygonCollection.Select((Polygon pg) => Extent.Union((IEnumerable<Point>)pg))));
		for (int intervalIndex = 0; intervalIndex < polygonCollection.Count; intervalIndex++)
		{
			Polygon polygon = polygonCollection[intervalIndex];
			spatialIndex.Add(polygon, intervalIndex);
		}
		return spatialIndex;
	}
}
