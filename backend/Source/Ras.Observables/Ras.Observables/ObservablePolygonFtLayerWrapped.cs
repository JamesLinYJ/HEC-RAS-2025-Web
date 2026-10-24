using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers;
using Utility;
using Utility.Hashing;

namespace Ras.Observables;

public class ObservablePolygonFtLayerWrapped<GeometryLayer, Feature, ObservableFeature>(ObservableFeatureLayer<GeometryLayer, Feature, ObservableFeature> lyr, Func<Feature, Polygon> featureToVector, Func<Polygon, Feature> vectorToFeature) : ObservableFeatureLayerWrapped<GeometryLayer, Feature, ObservableFeature, Polygon>(lyr, featureToVector, vectorToFeature), IPolygonCollection, IVectorCollection<Polygon>, IReadOnlyVectorCollection<Polygon>, IEnumerable<Polygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polygon>, IReadOnlyCollection<Polygon>, IReadOnlyPolygonCollection, ISpatiallyIndexedSegmentCollection, ILineSegmentSource where GeometryLayer : FeatureLayerBase<Feature>, IDataHash where Feature : class, IFeatureRow, new() where ObservableFeature : ObservableObject, IObservableFeature<Feature>
{
	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, ComputeSegmentSpatialIndex);
	}

	private SpatialIndex<FeatureSegmentSubset> ComputeSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex((IList<Polygon>)this.ToArray(), (RasterDefinition?)null);
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _spIdxCache.Get(VersionXY, ComputeSpatialIndex);
	}

	private SpatialIndex<int> ComputeSpatialIndex()
	{
		return SpatialIndex.ComputeSpatialIndex(this.ToArray());
	}
}
