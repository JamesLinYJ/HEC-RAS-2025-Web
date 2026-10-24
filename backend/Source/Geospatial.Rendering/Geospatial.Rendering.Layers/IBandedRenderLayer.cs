using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface IBandedRenderLayer : IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider
{
}
