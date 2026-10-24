using System;
using System.Collections.Generic;

namespace Geospatial.PairedData;

public interface IDiscreteVelocityTimeSeries
{
	IReadOnlyList<DateTime> Times { get; }

	IReadOnlyList<double> XValues { get; }

	IReadOnlyList<double> YValues { get; }

	void Sample(DateTime time, out double vx, out double vy);

	void Sample(IList<DateTime> times, out double[] vx, out double[] vy);
}
