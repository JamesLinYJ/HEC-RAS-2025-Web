using System.Diagnostics;
using Geospatial.Vectors;

namespace Geospatial.PairedData;

[DebuggerDisplay("{Location}: {TimeSeries.Values.Count} Values")]
public class TimeSeriesGauge
{
	public IDiscreteTimeSeries TimeSeries { get; }

	public Point Location { get; }

	public TimeSeriesGauge(IDiscreteTimeSeries timeSeries, Point loc)
	{
		TimeSeries = timeSeries;
		Location = loc;
	}
}
