using System;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface IDiscreteTimeSeriesRenderLayer : ITimeSeriesRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName
{
	bool CanInterpolate { get; }

	DateTime[] Timesteps { get; }

	bool TrySetTime(int timestep);
}
