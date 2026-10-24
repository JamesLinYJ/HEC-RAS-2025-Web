using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.TiledRenderingSystem;

public struct RenderTaskConstructionDefinition
{
	internal RenderTaskDescriptor TaskDescriptor { get; }

	public RasterDefinition TileDefinition { get; }

	public IList<IRenderLayer> Layers { get; }

	public RenderTaskConstructionDefinition(PyramidLevel pl, Pixel tileIndex, long renderState, RasterDefinition tileDef, IList<IRenderLayer> layers)
	{
		TaskDescriptor = new RenderTaskDescriptor(renderState, pl, tileIndex);
		TileDefinition = tileDef;
		Layers = layers;
	}
}
