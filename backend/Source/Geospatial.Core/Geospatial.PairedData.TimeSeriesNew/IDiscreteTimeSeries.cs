using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public interface IDiscreteTimeSeries<T> : ITimeSeries<T>, IFunctionalSeries<long, T>, IDiscreteSeries<long, T> where T : unmanaged, IEquatable<T>
{
}
