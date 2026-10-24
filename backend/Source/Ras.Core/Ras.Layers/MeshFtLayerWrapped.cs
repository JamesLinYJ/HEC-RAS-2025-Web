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

internal class MeshFtLayerWrapped<F>(GeometryLayerBase<F> lyr, Func<F, Mesh> featureToVector, Func<Mesh, F> vectorToFeature) : FeatureLayerWrapped<F, Mesh>((FeatureLayerBase<F>)lyr, featureToVector, vectorToFeature), IMeshCollection, IVectorCollection<Mesh>, IReadOnlyVectorCollection<Mesh>, IEnumerable<Mesh>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Mesh>, IReadOnlyCollection<Mesh> where F : IFeatureRow, new()
{
	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, ComputeSegmentSpatialIndex);
	}

	private SpatialIndex<FeatureSegmentSubset> ComputeSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex((IList<Polygon>)this.Select((Mesh m) => m.Perimeter).ToArray(), (RasterDefinition?)null);
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _spIdxCache.Get(VersionXY, ComputeSpatialIndex);
	}

	private SpatialIndex<int> ComputeSpatialIndex()
	{
		return SpatialIndex.ComputeSpatialIndex(this.Select((Mesh m) => m.Perimeter).ToArray());
	}
}
