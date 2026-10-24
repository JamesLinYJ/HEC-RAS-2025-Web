using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public interface IDiscreteSeries<TIn, TOut> : IFunctionalSeries<TIn, TOut> where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
{
	ReadOnlyMemory<TIn> Inputs { get; }

	ReadOnlyMemory<TOut> Values { get; }
}
