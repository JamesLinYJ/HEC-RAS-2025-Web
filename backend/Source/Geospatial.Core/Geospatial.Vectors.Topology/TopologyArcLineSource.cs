using Geospatial.DataStructures;

namespace Geospatial.Vectors.Topology;

public sealed class TopologyArcLineSource : ILineSegmentSource
{
	private readonly Topology _topo;

	public TopologyArcLineSource(Topology topo)
	{
		_topo = topo;
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _topo.ArcSpatialIndex;
	}

	public Polyline GetLine(int fid)
	{
		return _topo.GetArcPolyline(fid);
	}
}
