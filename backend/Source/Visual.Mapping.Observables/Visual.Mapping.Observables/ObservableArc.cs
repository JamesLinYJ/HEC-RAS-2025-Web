using Geospatial.Vectors.Topology;

namespace Visual.Mapping.Observables;

public class ObservableArc : ObservableBaseArc<Arc>
{
	public override Arc Arc => base.Arc;

	public ObservableArc(ObservableTopology topo, int fid)
		: base((ObservableBaseTopology)topo, fid)
	{
	}
}
