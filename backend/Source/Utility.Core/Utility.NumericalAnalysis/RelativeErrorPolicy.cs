using System;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public readonly struct RelativeErrorPolicy<T> where T : IFloatingPointIeee754<T>
{
	public T RelativeTolerance { get; }

	public T FloorFraction { get; }

	public RelativeErrorPolicy(T relativeTolerance, T floorFraction)
	{
		if (!T.IsFinite(relativeTolerance) || relativeTolerance < T.Zero)
		{
			throw new ArgumentOutOfRangeException("relativeTolerance");
		}
		if (!T.IsFinite(floorFraction) || floorFraction < T.Zero)
		{
			throw new ArgumentOutOfRangeException("floorFraction");
		}
		RelativeTolerance = relativeTolerance;
		FloorFraction = floorFraction;
	}

	public T[] CreateDenominators(ReadOnlySpan<T> values)
	{
		T zero = T.Zero;
		for (int i = 0; i < values.Length; i++)
		{
			if (!T.IsFinite(values[i]))
			{
				throw new ArgumentException("All values must be finite.", "values");
			}
			zero = T.Max(zero, T.Abs(values[i]));
		}
		T y = FloorFraction * zero;
		T[] denominators = new T[values.Length];
		for (int j = 0; j < values.Length; j++)
		{
			denominators[j] = T.Max(T.Abs(values[j]), y);
		}
		return denominators;
	}

	public T[] CreateMaximumAbsoluteErrors(ReadOnlySpan<T> values)
	{
		T[] denominators = CreateDenominators(values);
		for (int i = 0; i < denominators.Length; i++)
		{
			T[] denominatorsArray = denominators;
			int ordinal = i;
			denominatorsArray[ordinal] *= RelativeTolerance;
		}
		return denominators;
	}

	public T Error(T expected, T actual, T maximumMagnitude)
	{
		T expectedFloorFractionTerm = T.Max(T.Abs(expected), FloorFraction * maximumMagnitude);
		T absoluteActualAboveExpected = T.Abs(actual - expected);
		if (expectedFloorFractionTerm > T.Zero)
		{
			return absoluteActualAboveExpected / expectedFloorFractionTerm;
		}
		if (!(absoluteActualAboveExpected == T.Zero))
		{
			return T.PositiveInfinity;
		}
		return T.Zero;
	}
}
