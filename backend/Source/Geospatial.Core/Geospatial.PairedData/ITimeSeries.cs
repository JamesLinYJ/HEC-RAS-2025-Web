using System;
using System.Collections.Generic;

namespace Geospatial.PairedData;

public interface ITimeSeries
{
	TimeSeriesDataBehavior Behavior { get; }

	DateTime StartTime { get; }

	DateTime EndTime { get; }

	double Sample(DateTime time);

	double[] Sample(IList<DateTime> times);
}
