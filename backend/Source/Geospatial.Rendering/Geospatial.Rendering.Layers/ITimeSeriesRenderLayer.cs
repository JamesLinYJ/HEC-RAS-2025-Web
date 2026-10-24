using System;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface ITimeSeriesRenderLayer : IRenderLayer, IDisplayName, IReadOnlyDisplayName
{
	DateTime StartTime { get; }

	DateTime EndTime { get; }

	DateTime CurrentTime { get; }

	bool CurrentTimeIsValid { get; }

	bool TrySetTime(DateTime dt);
}
