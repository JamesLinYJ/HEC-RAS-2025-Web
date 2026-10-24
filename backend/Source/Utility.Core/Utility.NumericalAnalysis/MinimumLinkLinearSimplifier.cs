using System;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public static class MinimumLinkLinearSimplifier
{
	public static int[] FindKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> maximumAbsoluteErrors) where T : IFloatingPointIeee754<T>
	{
		return FindKnotIndexes(x, y, maximumAbsoluteErrors, ReadOnlySpan<int>.Empty);
	}

	public static int[] FindKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> maximumAbsoluteErrors, ReadOnlySpan<int> requiredKnotIndexes) where T : IFloatingPointIeee754<T>
	{
		int[] knotIndexes = new int[x.Length];
		int writeKnotIndexes = WriteKnotIndexes(x, y, maximumAbsoluteErrors, requiredKnotIndexes, knotIndexes);
		if (writeKnotIndexes != knotIndexes.Length)
		{
			Array.Resize(ref knotIndexes, writeKnotIndexes);
		}
		return knotIndexes;
	}

	public static int[] FindSharedKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T[]> valuesBySeries, ReadOnlySpan<T[]> maximumAbsoluteErrorsBySeries) where T : IFloatingPointIeee754<T>
	{
		return FindSharedKnotIndexes(x, valuesBySeries, maximumAbsoluteErrorsBySeries, ReadOnlySpan<int>.Empty);
	}

	public static int[] FindSharedKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T[]> valuesBySeries, ReadOnlySpan<T[]> maximumAbsoluteErrorsBySeries, ReadOnlySpan<int> requiredKnotIndexes) where T : IFloatingPointIeee754<T>
	{
		int[] sharedKnotIndexes = new int[x.Length];
		int writeSharedKnotIndexes = WriteSharedKnotIndexes(x, valuesBySeries, maximumAbsoluteErrorsBySeries, requiredKnotIndexes, sharedKnotIndexes);
		if (writeSharedKnotIndexes != sharedKnotIndexes.Length)
		{
			Array.Resize(ref sharedKnotIndexes, writeSharedKnotIndexes);
		}
		return sharedKnotIndexes;
	}

	public static int WriteKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> maximumAbsoluteErrors, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		return WriteKnotIndexes(x, y, maximumAbsoluteErrors, ReadOnlySpan<int>.Empty, destination);
	}

	public static int WriteKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> maximumAbsoluteErrors, ReadOnlySpan<int> requiredKnotIndexes, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		ValidateInputs(x, y, maximumAbsoluteErrors, requiredKnotIndexes, destination);
		if (x.Length == 0)
		{
			return 0;
		}
		if (x.Length == 1)
		{
			destination[0] = 0;
			return 1;
		}
		int[] linkCounts = new int[x.Length];
		int[] predecessors = new int[x.Length];
		int simplifySegmentX2Term = 0;
		int requiredKnotIndex = 0;
		for (int i = 0; i < requiredKnotIndexes.Length; i++)
		{
			int requiredKnotIndex2 = requiredKnotIndexes[i];
			if (requiredKnotIndex2 != requiredKnotIndex)
			{
				int ordinal = simplifySegmentX2Term;
				ReadOnlySpan<T> x2 = x;
				int start = requiredKnotIndex;
				bool omitStart = simplifySegmentX2Term > 0;
				int ordinal2 = simplifySegmentX2Term;
				simplifySegmentX2Term = ordinal + SimplifySegment(x2, y, maximumAbsoluteErrors, start, requiredKnotIndex2, omitStart, linkCounts, predecessors, destination.Slice(ordinal2, destination.Length - ordinal2));
				requiredKnotIndex = requiredKnotIndex2;
			}
		}
		if (requiredKnotIndex < x.Length - 1)
		{
			int ordinal3 = simplifySegmentX2Term;
			ReadOnlySpan<T> x3 = x;
			int start2 = requiredKnotIndex;
			int end = x.Length - 1;
			bool omitStart2 = simplifySegmentX2Term > 0;
			int ordinal4 = simplifySegmentX2Term;
			simplifySegmentX2Term = ordinal3 + SimplifySegment(x3, y, maximumAbsoluteErrors, start2, end, omitStart2, linkCounts, predecessors, destination.Slice(ordinal4, destination.Length - ordinal4));
		}
		return simplifySegmentX2Term;
	}

	public static int WriteSharedKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T[]> valuesBySeries, ReadOnlySpan<T[]> maximumAbsoluteErrorsBySeries, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		return WriteSharedKnotIndexes(x, valuesBySeries, maximumAbsoluteErrorsBySeries, ReadOnlySpan<int>.Empty, destination);
	}

	public static int WriteSharedKnotIndexes<T>(ReadOnlySpan<T> x, ReadOnlySpan<T[]> valuesBySeries, ReadOnlySpan<T[]> maximumAbsoluteErrorsBySeries, ReadOnlySpan<int> requiredKnotIndexes, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		ValidateSharedInputs(x, valuesBySeries, maximumAbsoluteErrorsBySeries, requiredKnotIndexes, destination);
		if (x.Length == 0)
		{
			return 0;
		}
		if (x.Length == 1)
		{
			destination[0] = 0;
			return 1;
		}
		int[] linkCounts = new int[x.Length];
		int[] predecessors = new int[x.Length];
		T[] minimumSlopes = new T[valuesBySeries.Length];
		T[] maximumSlopes = new T[valuesBySeries.Length];
		int simplifySharedSegmentX2Term = 0;
		int requiredKnotIndex = 0;
		for (int i = 0; i < requiredKnotIndexes.Length; i++)
		{
			int requiredKnotIndex2 = requiredKnotIndexes[i];
			if (requiredKnotIndex2 != requiredKnotIndex)
			{
				int ordinal = simplifySharedSegmentX2Term;
				ReadOnlySpan<T> x2 = x;
				ReadOnlySpan<T[]> valuesBySeries2 = valuesBySeries;
				int start = requiredKnotIndex;
				bool omitStart = simplifySharedSegmentX2Term > 0;
				int ordinal2 = simplifySharedSegmentX2Term;
				simplifySharedSegmentX2Term = ordinal + SimplifySharedSegment(x2, valuesBySeries2, maximumAbsoluteErrorsBySeries, start, requiredKnotIndex2, omitStart, linkCounts, predecessors, minimumSlopes, maximumSlopes, destination.Slice(ordinal2, destination.Length - ordinal2));
				requiredKnotIndex = requiredKnotIndex2;
			}
		}
		if (requiredKnotIndex < x.Length - 1)
		{
			int ordinal3 = simplifySharedSegmentX2Term;
			ReadOnlySpan<T> x3 = x;
			ReadOnlySpan<T[]> valuesBySeries3 = valuesBySeries;
			int start2 = requiredKnotIndex;
			int end = x.Length - 1;
			bool omitStart2 = simplifySharedSegmentX2Term > 0;
			int ordinal4 = simplifySharedSegmentX2Term;
			simplifySharedSegmentX2Term = ordinal3 + SimplifySharedSegment(x3, valuesBySeries3, maximumAbsoluteErrorsBySeries, start2, end, omitStart2, linkCounts, predecessors, minimumSlopes, maximumSlopes, destination.Slice(ordinal4, destination.Length - ordinal4));
		}
		return simplifySharedSegmentX2Term;
	}

	private static int SimplifySegment<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> maximumAbsoluteErrors, int start, int end, bool omitStart, int[] linkCounts, int[] predecessors, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		MinimumLinkSimplifier.InitializePath(start, end, linkCounts, predecessors);
		for (int i = start; i < end; i++)
		{
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
				T y2 = (y[j] - maximumAbsoluteErrors[j] - y[i]) / localValue;
				T y3 = (y[j] + maximumAbsoluteErrors[j] - y[i]) / localValue;
				negativeInfinity = T.Max(negativeInfinity, y2);
				positiveInfinity = T.Min(positiveInfinity, y3);
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

	private static int SimplifySharedSegment<T>(ReadOnlySpan<T> x, ReadOnlySpan<T[]> valuesBySeries, ReadOnlySpan<T[]> maximumAbsoluteErrorsBySeries, int start, int end, bool omitStart, int[] linkCounts, int[] predecessors, T[] minimumSlopes, T[] maximumSlopes, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		MinimumLinkSimplifier.InitializePath(start, end, linkCounts, predecessors);
		for (int i = start; i < end; i++)
		{
			Array.Fill(minimumSlopes, T.NegativeInfinity);
			Array.Fill(maximumSlopes, T.PositiveInfinity);
			for (int j = i + 1; j <= end; j++)
			{
				T localValue = x[j] - x[i];
				bool condition = true;
				for (int k = 0; k < valuesBySeries.Length; k++)
				{
					T[] valuesBySery = valuesBySeries[k];
					T valuesBySeryTerm = (valuesBySery[j] - valuesBySery[i]) / localValue;
					if (valuesBySeryTerm < minimumSlopes[k] || valuesBySeryTerm > maximumSlopes[k])
					{
						condition = false;
					}
				}
				if (condition)
				{
					int linkCounts2 = linkCounts[i] + 1;
					if (MinimumLinkSimplifier.ShouldReplacePath(linkCounts2, i, linkCounts[j], predecessors[j]))
					{
						linkCounts[j] = linkCounts2;
						predecessors[j] = i;
					}
				}
				bool condition2 = false;
				for (int l = 0; l < valuesBySeries.Length; l++)
				{
					T[] valuesBySeryArray = valuesBySeries[l];
					T[] maximumAbsoluteErrorsBySery = maximumAbsoluteErrorsBySeries[l];
					T y = (valuesBySeryArray[j] - maximumAbsoluteErrorsBySery[j] - valuesBySeryArray[i]) / localValue;
					T y2 = (valuesBySeryArray[j] + maximumAbsoluteErrorsBySery[j] - valuesBySeryArray[i]) / localValue;
					minimumSlopes[l] = T.Max(minimumSlopes[l], y);
					maximumSlopes[l] = T.Min(maximumSlopes[l], y2);
					if (minimumSlopes[l] > maximumSlopes[l])
					{
						condition2 = true;
					}
				}
				if (condition2)
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

	private static void ValidateInputs<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> maximumAbsoluteErrors, ReadOnlySpan<int> requiredKnotIndexes, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		if (x.Length != y.Length || x.Length != maximumAbsoluteErrors.Length)
		{
			throw new ArgumentException("The x, y, and maximum-error spans must have equal lengths.");
		}
		if (destination.Length < x.Length)
		{
			throw new ArgumentException("The destination must have room for every source index.", "destination");
		}
		for (int i = 0; i < x.Length; i++)
		{
			if (!T.IsFinite(x[i]))
			{
				throw new ArgumentException("All x values must be finite.", "x");
			}
			if (!T.IsFinite(y[i]))
			{
				throw new ArgumentException("All y values must be finite.", "y");
			}
			if (!T.IsFinite(maximumAbsoluteErrors[i]) || maximumAbsoluteErrors[i] < T.Zero)
			{
				throw new ArgumentException("All maximum errors must be finite and nonnegative.", "maximumAbsoluteErrors");
			}
			if (i > 0 && x[i] <= x[i - 1])
			{
				throw new ArgumentException("The x values must be strictly increasing.", "x");
			}
		}
		int requiredKnotIndexTerm = -1;
		for (int j = 0; j < requiredKnotIndexes.Length; j++)
		{
			int requiredKnotIndex = requiredKnotIndexes[j];
			if (requiredKnotIndex < 0 || requiredKnotIndex >= x.Length)
			{
				throw new ArgumentOutOfRangeException("requiredKnotIndexes", "Required knot indexes must refer to source points.");
			}
			if (requiredKnotIndex <= requiredKnotIndexTerm)
			{
				throw new ArgumentException("Required knot indexes must be sorted and unique.", "requiredKnotIndexes");
			}
			requiredKnotIndexTerm = requiredKnotIndex;
		}
	}

	private static void ValidateSharedInputs<T>(ReadOnlySpan<T> x, ReadOnlySpan<T[]> valuesBySeries, ReadOnlySpan<T[]> maximumAbsoluteErrorsBySeries, ReadOnlySpan<int> requiredKnotIndexes, Span<int> destination) where T : IFloatingPointIeee754<T>
	{
		if (valuesBySeries.Length == 0)
		{
			throw new ArgumentException("At least one value series is required.", "valuesBySeries");
		}
		if (valuesBySeries.Length != maximumAbsoluteErrorsBySeries.Length)
		{
			throw new ArgumentException("Value and maximum-error series counts must be equal.", "maximumAbsoluteErrorsBySeries");
		}
		if (destination.Length < x.Length)
		{
			throw new ArgumentException("The destination must have room for every source index.", "destination");
		}
		for (int i = 0; i < x.Length; i++)
		{
			if (!T.IsFinite(x[i]))
			{
				throw new ArgumentException("All x values must be finite.", "x");
			}
			if (i > 0 && x[i] <= x[i - 1])
			{
				throw new ArgumentException("The x values must be strictly increasing.", "x");
			}
		}
		for (int j = 0; j < valuesBySeries.Length; j++)
		{
			T[] valuesBySery = valuesBySeries[j];
			T[] maximumAbsoluteErrorsBySery = maximumAbsoluteErrorsBySeries[j];
			if (valuesBySery == null || valuesBySery.Length != x.Length)
			{
				throw new ArgumentException("Every value series must match the x length.", "valuesBySeries");
			}
			if (maximumAbsoluteErrorsBySery == null || maximumAbsoluteErrorsBySery.Length != x.Length)
			{
				throw new ArgumentException("Every maximum-error series must match the x length.", "maximumAbsoluteErrorsBySeries");
			}
			for (int k = 0; k < x.Length; k++)
			{
				if (!T.IsFinite(valuesBySery[k]))
				{
					throw new ArgumentException("All values must be finite.", "valuesBySeries");
				}
				if (!T.IsFinite(maximumAbsoluteErrorsBySery[k]) || maximumAbsoluteErrorsBySery[k] < T.Zero)
				{
					throw new ArgumentException("All maximum errors must be finite and nonnegative.", "maximumAbsoluteErrorsBySeries");
				}
			}
		}
		int requiredKnotIndexTerm = -1;
		for (int l = 0; l < requiredKnotIndexes.Length; l++)
		{
			int requiredKnotIndex = requiredKnotIndexes[l];
			if (requiredKnotIndex < 0 || requiredKnotIndex >= x.Length)
			{
				throw new ArgumentOutOfRangeException("requiredKnotIndexes", "Required knot indexes must refer to source points.");
			}
			if (requiredKnotIndex <= requiredKnotIndexTerm)
			{
				throw new ArgumentException("Required knot indexes must be sorted and unique.", "requiredKnotIndexes");
			}
			requiredKnotIndexTerm = requiredKnotIndex;
		}
	}
}
