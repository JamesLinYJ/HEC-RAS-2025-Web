using Geospatial.Vectors.Topology;

namespace Visual.Mapping.Observables;

public class ObservableNode : ObservableTopologyFeature
{
	private Node _node;

	public Node Node => _node;

	public ObservableNode(ObservableBaseTopology topo, int fid)
		: base(topo, fid, topo.NodeMetadata)
	{
		_node = topo.Topology.Nodes[fid];
	}
}
