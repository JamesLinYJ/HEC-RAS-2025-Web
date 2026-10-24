using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility;

namespace Ras.Layers;

internal class PolylineFtLayerWrapped<F>(FeatureLayerBase<F> lyr, Func<F, Polyline> featureToVector, Func<Polyline, F> vectorToFeature) : FeatureLayerWrapped<F, Polyline>(lyr, featureToVector, vectorToFeature), IPolylineCollection, IVectorCollection<Polyline>, IReadOnlyVectorCollection<Polyline>, IEnumerable<Polyline>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polyline>, IReadOnlyCollection<Polyline>, ISpatiallyIndexedSegmentCollection, ILineSegmentSource where F : IFeatureRow, new()
{
	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, ComputeSegmentSpatialIndex);
	}

	private SpatialIndex<FeatureSegmentSubset> ComputeSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex<Polyline>((IList<Polyline>)this.ToArray(), (RasterDefinition?)null);
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
