using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public class VirtualPointRenderLayer : PointRenderLayer
{
	public VirtualPointRenderLayer(IList<Point> points)
		: base(points, mutabilityFakeArgument: true)
	{
	}
}
