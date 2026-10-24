using Geospatial.Vectors.Graph;

namespace Visual.Mapping.Observables;

public class ObservableGraphArc : ObservableBaseArc<GraphArc>
{
	public ObservableGraphArc(ObservableBaseTopology topo, int fid)
		: base(topo, fid)
	{
	}
}
