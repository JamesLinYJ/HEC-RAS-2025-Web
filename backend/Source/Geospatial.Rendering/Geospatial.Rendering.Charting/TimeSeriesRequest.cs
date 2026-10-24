using System;
using System.Linq;
using Geospatial.PairedData;

namespace Geospatial.Rendering.Charting;

public class TimeSeriesRequest : SeriesRequest, ITimeSeriesRequest, ISeriesRequest
{
	public DateTime[] Times { get; }

	public double[] YData { get; }

	public TimeSeriesRequest(string name, string yLabel, string yUnits, DateTime[] times, double[] yData)
		: base(name, "", "Time", yLabel, yUnits)
	{
		Times = times;
		YData = yData;
	}

	public TimeSeriesRequest(string name, string yLabel, string yUnits, IDiscreteTimeSeries ts)
		: base(name, "", "Time", yLabel, yUnits)
	{
		Times = ts.Times.ToArray();
		YData = ts.Values.ToArray();
	}
}
