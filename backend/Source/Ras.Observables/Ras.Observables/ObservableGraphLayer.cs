using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers;
using Visual.Mapping.Observables;

namespace Ras.Observables;

public class ObservableGraphLayer : ObservableObject
{
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableGraph Graph
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<ObservableGraph>.Default.Equals(field, value))
			{
				OnPropertyChanging("Graph");
				field = value;
				OnPropertyChanged("Graph");
			}
		}
	}

	public ObservableGraphLayer(GraphLayer graph)
	{
		Graph = new ObservableGraph(graph.Graph);
	}
}
