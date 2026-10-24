using Geospatial.Vectors;

namespace Geospatial.PairedData;

public class AccumulatedGauge : TimeSeriesGauge
{
	public new IAccumulatedTimeSeries TimeSeries { get; }

	public AccumulatedGauge(IAccumulatedTimeSeries timeSeries, Point loc)
		: base(timeSeries, loc)
	{
		TimeSeries = timeSeries;
	}
}
