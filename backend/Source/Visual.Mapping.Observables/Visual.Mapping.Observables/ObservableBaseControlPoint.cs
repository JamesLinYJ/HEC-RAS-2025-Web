using Geospatial.Vectors;

namespace Visual.Mapping.Observables;

public abstract class ObservableBaseControlPoint : ObservableTopologyFeature, ITaggedTopologyFeature
{
	private Point _controlPoint;

	public override ObservableTopology Topology => base.Topology as ObservableTopology;

	public Point ControlPoint => _controlPoint;

	public ObservableBaseControlPoint(ObservableTopology topo, int fid)
		: base(topo, fid, topo.ControlPointMetadata)
	{
		_controlPoint = topo.Topology.RegionControlPoints[fid];
		BuildTags(Topology.ControlPointTagDefinitions);
	}
}
