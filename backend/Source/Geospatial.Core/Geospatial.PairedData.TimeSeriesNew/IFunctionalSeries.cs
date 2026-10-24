using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public interface IFunctionalSeries<TIn, TOut> where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
{
	TOut? NoDataValue { get; }

	TIn LowerBound { get; }

	TIn UpperBound { get; }

	TOut Sample(TIn input);

	void Sample(Memory<TIn> inputs, Memory<TOut> outData);
}
