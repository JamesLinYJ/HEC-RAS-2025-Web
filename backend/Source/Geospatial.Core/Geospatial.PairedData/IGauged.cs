using System.Collections.Generic;

namespace Geospatial.PairedData;

public interface IGauged
{
	List<TimeSeriesGauge> Gauges { get; }
}
