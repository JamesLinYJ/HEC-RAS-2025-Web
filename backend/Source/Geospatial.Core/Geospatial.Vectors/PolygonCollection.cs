using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Vectors.Polygons;
using Utility;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebugDisplayString()}")]
public class PolygonCollection : VectorCollectionBase<Polygon>, IPolygonCollection, IVectorCollection<Polygon>, IReadOnlyVectorCollection<Polygon>, IEnumerable<Polygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polygon>, IReadOnlyCollection<Polygon>, IReadOnlyPolygonCollection, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	private readonly Versioned<double> _areaCache = new Versioned<double>();

	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	public IReadOnlyList<Segment> Segments { get; private set; }

	public double TotalArea => _areaCache.Get(VersionXY, ComputeArea);

	public PolygonCollection()
	{
	}

	public PolygonCollection(int capacity)
		: base(capacity)
	{
	}

	public PolygonCollection(Polygon poly)
		: base(poly)
	{
	}

	public PolygonCollection(IEnumerable<Polygon> polygons)
		: base(polygons)
	{
	}

	protected override void Init()
	{
		Segments = new VirtualSegmentCollection(_elements);
	}

	public int[] StableSort()
	{
		return Polygon.StableSort(_elements);
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, CreateSegmentSpatialIndex);
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _spIdxCache.Get(VersionXY, CreateAreaSpatialIndex);
	}

	public Polyline GetLine(int fid)
	{
		return base[fid];
	}

	private SpatialIndex<FeatureSegmentSubset> CreateSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex(_elements);
	}

	private SpatialIndex<int> CreateAreaSpatialIndex()
	{
		return SpatialIndex.FromPolygons(_elements);
	}

	private double ComputeArea()
	{
		return _elements.Sum((Polygon pg) => pg.Area);
	}

	private string DebugDisplayString()
	{
		return $"{Count} Polygons. Area={TotalArea}";
	}
}
