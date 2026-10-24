using System;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public static class MinimumLinkBoundedLinearSimplifier
{
	public static int[] FindKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> lowerBounds, ReadOnlySpan<T> upperBounds, ReadOnlySpan<int> requiredKnotIndexes = default(ReadOnlySpan<int>)) where T : IFloatingPointIeee754<T>
	{
		Validate(x, y, lowerBounds, upperBounds, requiredKnotIndexes);
		if (x.Length == 0)
		{
			return Array.Empty<int>();
		}
		if (x.Length == 1)
		{
			return new int[1];
		}
		int[] linkCounts = new int[x.Length];
		int[] predecessors = new int[x.Length];
		int[] intValues = new int[x.Length];
		int solvePartitionSum = 0;
		int requiredKnotIndex = 0;
		for (int i = 0; i <= requiredKnotIndexes.Length; i++)
		{
			int requiredKnotIndex2 = ((i < requiredKnotIndexes.Length) ? requiredKnotIndexes[i] : (x.Length - 1));
			if (requiredKnotIndex2 > requiredKnotIndex)
			{
				int solvePartition = SolvePartition(x, y, lowerBounds, upperBounds, requiredKnotIndex, requiredKnotIndex2, solvePartitionSum > 0, linkCounts, predecessors, intValues.AsSpan(solvePartitionSum));
				solvePartitionSum += solvePartition;
				requiredKnotIndex = requiredKnotIndex2;
			}
		}
		Array.Resize(ref intValues, solvePartitionSum);
		return intValues;
	}

	private static int SolvePartition<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> lowerBounds, ReadOnlySpan<T> upperBounds, int start, int end, bool omitStart, int[] linkCounts, int[] predecessors, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		MinimumLinkSimplifier.InitializePath(start, end, linkCounts, predecessors);
		for (int i = start; i < end; i++)
		{
			if (linkCounts[i] == int.MaxValue)
			{
				continue;
			}
			T negativeInfinity = T.NegativeInfinity;
			T positiveInfinity = T.PositiveInfinity;
			for (int j = i + 1; j <= end; j++)
			{
				T localValue = x[j] - x[i];
				T localValue2 = (y[j] - y[i]) / localValue;
				if (localValue2 >= negativeInfinity && localValue2 <= positiveInfinity)
				{
					int linkCounts2 = linkCounts[i] + 1;
					if (MinimumLinkSimplifier.ShouldReplacePath(linkCounts2, i, linkCounts[j], predecessors[j]))
					{
						linkCounts[j] = linkCounts2;
						predecessors[j] = i;
					}
				}
				negativeInfinity = T.Max(negativeInfinity, (lowerBounds[j] - y[i]) / localValue);
				positiveInfinity = T.Min(positiveInfinity, (upperBounds[j] - y[i]) / localValue);
				if (negativeInfinity > positiveInfinity)
				{
					break;
				}
			}
		}
		int linkCountsEndTerm = linkCounts[end] + 1;
		int omitStartTerm = (omitStart ? (linkCountsEndTerm - 1) : linkCountsEndTerm);
		int ordinal = omitStartTerm - 1;
		for (int end2 = end; end2 != start; end2 = predecessors[end2])
		{
			destination[ordinal--] = end2;
		}
		if (!omitStart)
		{
			destination[0] = start;
		}
		return omitStartTerm;
	}

	private static void Validate<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> lowerBounds, ReadOnlySpan<T> upperBounds, ReadOnlySpan<int> requiredKnotIndexes) where T : IFloatingPointIeee754<T>
	{
		if (x.Length != y.Length || x.Length != lowerBounds.Length || x.Length != upperBounds.Length)
		{
			throw new ArgumentException("Coordinates, values, and bounds must have equal lengths.");
		}
		for (int i = 0; i < x.Length; i++)
		{
			if (!T.IsFinite(x[i]) || !T.IsFinite(y[i]) || !T.IsFinite(lowerBounds[i]) || !T.IsFinite(upperBounds[i]))
			{
				throw new ArgumentException("Coordinates, values, and bounds must be finite.");
			}
			if (lowerBounds[i] > upperBounds[i])
			{
				throw new ArgumentException("Each lower bound must be no greater than its upper bound.");
			}
			if (y[i] < lowerBounds[i] || y[i] > upperBounds[i])
			{
				throw new ArgumentException("Each source value must lie within its bounds.", "y");
			}
			if (i > 0 && x[i] <= x[i - 1])
			{
				throw new ArgumentException("Coordinates must be strictly increasing.", "x");
			}
		}
		int requiredKnotIndexTerm = -1;
		for (int j = 0; j < requiredKnotIndexes.Length; j++)
		{
			int requiredKnotIndex = requiredKnotIndexes[j];
			if (requiredKnotIndex < 0 || requiredKnotIndex >= x.Length)
			{
				throw new ArgumentOutOfRangeException("requiredKnotIndexes");
			}
			if (requiredKnotIndex <= requiredKnotIndexTerm)
			{
				throw new ArgumentException("Required knot indexes must be sorted and unique.", "requiredKnotIndexes");
			}
			requiredKnotIndexTerm = requiredKnotIndex;
		}
	}
}
