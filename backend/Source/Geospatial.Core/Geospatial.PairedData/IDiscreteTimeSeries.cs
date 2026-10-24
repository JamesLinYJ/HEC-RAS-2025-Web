using System;
using System.Collections.Generic;

namespace Geospatial.PairedData;

public interface IDiscreteTimeSeries : ITimeSeries
{
	IReadOnlyList<DateTime> Times { get; }

	IReadOnlyList<double> Values { get; }

	void Shift(TimeSpan deltaTime);

	void Scale(double scaleFactor);
}
