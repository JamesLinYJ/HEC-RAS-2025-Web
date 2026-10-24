using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface IRenderLayer : IDisplayName, IReadOnlyDisplayName
{
	Extent ValidExtent { get; }

	long GetRenderState();

	bool Render(PlotRequest plot, ref object cache);

	List<MapHover> MapHover(HitTestArgs hitTest, ref object hitTestCache);

	Dictionary<string, List<ILabelDefinition>> GetEnabledScreenAnnotations(RasterDefinition raster);
}
