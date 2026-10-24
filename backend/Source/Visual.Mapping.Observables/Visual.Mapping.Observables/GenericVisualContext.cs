using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Utility.Attributes;
using Visual.Mapping.Observables.Map;

namespace Visual.Mapping.Observables;

[Identifier("GenericLayer")]
public class GenericVisualContext : VisualContext
{
	private string _iconResource = NamedResources.Layer.Default;

	private IRenderLayer _initializedRenderLayer;

	public override string IconResource => _iconResource;

	public override string DefaultName => "Layer";

	public GenericVisualContext(IRenderLayer renderLayer, object context, MapBase map, string iconResource = null)
		: base(map)
	{
		_initializedRenderLayer = renderLayer;
		Context = context;
		if (iconResource != null)
		{
			_iconResource = iconResource;
		}
	}

	protected override EditingSession CreateEditLayerCore(ToolOrchestrator editLayer)
	{
		return null;
	}

	protected override IRenderLayer CreateRenderLayer()
	{
		return _initializedRenderLayer;
	}
}
