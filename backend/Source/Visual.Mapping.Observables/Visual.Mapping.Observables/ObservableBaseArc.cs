using System;
using Geospatial.Vectors.Topology;

namespace Visual.Mapping.Observables;

public abstract class ObservableBaseArc : ObservableTopologyFeature, ITaggedTopologyFeature
{
	private BaseArc _arc;

	public virtual BaseArc Arc => _arc;

	public double Length
	{
		get
		{
			if (Fid < Topology.Topology.ArcCount)
			{
				return Topology.Topology.GetArcPolyline(Fid).Length;
			}
			return 0.0;
		}
	}

	public ObservableBaseArc(ObservableBaseTopology topo, int fid)
		: base(topo, fid, topo.ArcMetadata)
	{
		_arc = Topology.Topology.Arcs[fid];
		Fid = fid;
		BuildTags(Topology.ArcTagDefinitions);
	}
}
public abstract class ObservableBaseArc<TArc> : ObservableBaseArc where TArc : BaseArc, IEquatable<TArc>, new()
{
	public override TArc Arc => base.Arc as TArc;

	public ObservableBaseArc(ObservableBaseTopology topo, int fid)
		: base(topo, fid)
	{
	}
}
