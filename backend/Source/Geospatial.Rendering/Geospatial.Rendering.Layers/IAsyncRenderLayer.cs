using System.Threading.Tasks;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface IAsyncRenderLayer : IRenderLayer, IDisplayName, IReadOnlyDisplayName
{
	Task<bool> RenderAsync(PlotRequest plot);
}
