using System;
using System.Linq;
using Geospatial.PairedData;
using Utility;

namespace Geospatial.Rendering.Charting;

public class VirtualTimeSeriesRequest : SeriesRequest, ITimeSeriesRequest, ISeriesRequest
{
	private LazyThreadsafe<IDiscreteTimeSeries> _threadsafeTs;

	private DateTime[] _times;

	private double[] _values;

	public DateTime[] Times
	{
		get
		{
			EnsureDataLoaded();
			return _times;
		}
	}

	public double[] YData
	{
		get
		{
			EnsureDataLoaded();
			return _values;
		}
	}

	public VirtualTimeSeriesRequest(string name, string yLabel, string yUnits, Func<IDiscreteTimeSeries> tsFunc)
		: base(name, "", "Time", yLabel, yUnits)
	{
		_threadsafeTs = new LazyThreadsafe<IDiscreteTimeSeries>(tsFunc);
	}

	private void EnsureDataLoaded()
	{
		IDiscreteTimeSeries discreteTimeSeries = _threadsafeTs.Acquire();
		_times = discreteTimeSeries.Times.ToArray();
		_values = discreteTimeSeries.Values.ToArray();
	}
}
