using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Visual.Mapping.Observables;

namespace Ras.Observables;

public abstract class ObservableTopologyLayer : ObservableObject
{
	private TopologyLayer _layer;

	public TopologyLayer Layer => _layer;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableTopology Topology
	{
		get
		{
			return field;
		}
		protected set
		{
			if (!EqualityComparer<ObservableTopology>.Default.Equals(field, value))
			{
				OnPropertyChanging("Topology");
				field = value;
				OnPropertyChanged("Topology");
			}
		}
	}

	public ObservableTopologyLayer(TopologyLayer topo, bool createTopology = true)
	{
		_layer = topo;
		if (createTopology)
		{
			Topology = CreateTopology((topo != null) ? topo.Topology : new Topology());
		}
	}

	protected abstract ObservableTopology CreateTopology(Topology topology);
}
