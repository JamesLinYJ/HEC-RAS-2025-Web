using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public interface ITimeSeries<T> : IFunctionalSeries<long, T> where T : unmanaged, IEquatable<T>
{
	TimeWindow TimeWindow { get; }
}
