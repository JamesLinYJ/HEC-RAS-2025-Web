using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Systems.Annotations;

public struct LayerToolTip
{
	public IRenderLayer RenderLayer { get; set; }

	public object ToolTipValue { get; set; }
}
