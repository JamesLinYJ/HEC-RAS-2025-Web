using Geospatial.Rendering.Layers;

namespace Visual.Mapping.Observables;

public class ObservableVectorRenderLayer : ObservableRenderLayer
{
	public override IVectorRenderLayer Layer => (IVectorRenderLayer)base.Layer;

	public int Count => Layer.Count;

	public ObservableVectorRenderLayer(IVectorRenderLayer layer)
		: base(layer)
	{
	}
}
