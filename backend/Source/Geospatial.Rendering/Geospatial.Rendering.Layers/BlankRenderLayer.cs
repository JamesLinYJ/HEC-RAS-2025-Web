using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public class BlankRenderLayer : RenderLayer
{
	public override Extent ValidExtent => Extent.NullExtent;

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		return true;
	}
}
