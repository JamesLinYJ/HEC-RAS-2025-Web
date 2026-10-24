using System;
using System.Threading.Tasks;

namespace Geospatial.Rendering.Layers;

public struct AsyncRenderResult
{
	public bool AnythingPlotted;

	public Func<PlotRequest, Task<AsyncRenderResult>> ContinuationRender;
}
