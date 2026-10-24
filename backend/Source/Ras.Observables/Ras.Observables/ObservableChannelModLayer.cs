using Geospatial.Vectors.Topology;
using Visual.Mapping.Observables;

namespace Ras.Observables;

public class ObservableChannelModLayer : ObservableTopologyLayer
{
	public ObservableChannelModTerrainInput TerrainInput { get; private set; }

	public ObservableChannelModLayer(ObservableChannelModTerrainInput ti)
		: base(ti.Layer.TopologyLayer, createTopology: false)
	{
		TerrainInput = ti;
		Topology = CreateTopology(ti.Layer.TopologyLayer.Topology);
	}

	protected override ObservableTopology CreateTopology(Topology topology)
	{
		return new ObservableChannelModTopology(TerrainInput, topology);
	}
}
