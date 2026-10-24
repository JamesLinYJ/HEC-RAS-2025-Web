using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface IRasterRenderLayer : IRenderLayer, IDisplayName, IReadOnlyDisplayName
{
	RasterSymbology RasterSymbology { get; set; }
}
