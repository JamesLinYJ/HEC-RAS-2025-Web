using System.Collections.Generic;

namespace Geospatial.Rendering.Charting;

public interface IChartRequest
{
	string Title { get; }

	IList<IList<ISeriesRequest>> Series { get; }
}
