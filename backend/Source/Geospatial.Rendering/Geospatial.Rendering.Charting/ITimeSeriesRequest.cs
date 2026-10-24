using System;

namespace Geospatial.Rendering.Charting;

public interface ITimeSeriesRequest : ISeriesRequest
{
	DateTime[] Times { get; }

	double[] YData { get; }
}
