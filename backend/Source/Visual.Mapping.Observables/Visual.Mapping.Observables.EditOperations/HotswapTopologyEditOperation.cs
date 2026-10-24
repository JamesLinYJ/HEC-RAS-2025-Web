using Geospatial.Rendering.Editing;
using Geospatial.Vectors.Topology;

namespace Visual.Mapping.Observables.EditOperations;

public class HotswapTopologyEditOperation : TopologyEditOperation
{
	private ObservableTopology _existingTopo;

	private Topology _existingTopoCloned;

	private Topology _newTopo;

	public override string Description => "Hot Swap Topology";

	public HotswapTopologyEditOperation(TopologyEditContext layer, ObservableTopology existingTopo, Topology newTopo)
		: base(layer, null)
	{
		_existingTopo = existingTopo;
		_existingTopoCloned = new Topology();
		existingTopo.CloneIntoBackingTopology(existingTopo.Topology, _existingTopoCloned);
		_newTopo = newTopo;
	}

	protected override bool ApplyInternal()
	{
		ClearAllSelection();
		_existingTopo.HotSwapTopology_WriteLockHeld(_newTopo);
		return true;
	}

	protected override bool UndoInternal()
	{
		ClearAllSelection();
		_existingTopo.HotSwapTopology_WriteLockHeld(_existingTopoCloned);
		return true;
	}
}
