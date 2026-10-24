using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering.Layers;

namespace Visual.Mapping.Observables;

public class ObservableRenderLayer : ObservableObject
{
	private IRenderLayer _layer;

	public virtual IRenderLayer Layer
	{
		get
		{
			return _layer;
		}
		private init
		{
			_layer = value;
		}
	}

	public ObservableRenderLayer(IRenderLayer layer)
	{
		Layer = layer;
	}
}
