using System;

namespace Utility.NumericalAnalysis;

public static class MinimumLinkSimplifier
{
	public static bool TryFind<TFit>(int pointCount, TryFitMinimumLinkSegment<TFit> tryFit, out MinimumLinkResult<TFit> result)
	{
		return TryFind(pointCount, ReadOnlySpan<int>.Empty, tryFit, out result);
	}

	public static bool TryFind<TFit>(int pointCount, ReadOnlySpan<int> requiredKnotIndexes, TryFitMinimumLinkSegment<TFit> tryFit, out MinimumLinkResult<TFit> result)
	{
		ValidateInputs(pointCount, requiredKnotIndexes, tryFit);
		switch (pointCount)
		{
		case 0:
			result = new MinimumLinkResult<TFit>(Array.Empty<int>(), Array.Empty<TFit>());
			return true;
		case 1:
			result = new MinimumLinkResult<TFit>(new int[1], Array.Empty<TFit>());
			return true;
		default:
		{
			int[] linkCounts = new int[pointCount];
			int[] predecessors = new int[pointCount];
			TFit[] predecessorFits = new TFit[pointCount];
			int[] segmentKnots = new int[pointCount];
			TFit[] segmentFits = new TFit[pointCount - 1];
			int[] intValues = new int[pointCount];
			TFit[] tFits = new TFit[pointCount - 1];
			int outputKnotCount = 0;
			int outputFitCount = 0;
			int requiredKnotIndex = 0;
			for (int i = 0; i < requiredKnotIndexes.Length; i++)
			{
				int requiredKnotIndex2 = requiredKnotIndexes[i];
				if (requiredKnotIndex2 != requiredKnotIndex)
				{
					if (!TrySolveSegment(requiredKnotIndex, requiredKnotIndex2, tryFit, linkCounts, predecessors, predecessorFits, segmentKnots, segmentFits, out var knotCount))
					{
						result = new MinimumLinkResult<TFit>(Array.Empty<int>(), Array.Empty<TFit>());
						return false;
					}
					AppendSegment(segmentKnots, segmentFits, knotCount, intValues, tFits, ref outputKnotCount, ref outputFitCount);
					requiredKnotIndex = requiredKnotIndex2;
				}
			}
			if (requiredKnotIndex < pointCount - 1)
			{
				if (!TrySolveSegment(requiredKnotIndex, pointCount - 1, tryFit, linkCounts, predecessors, predecessorFits, segmentKnots, segmentFits, out var knotCount2))
				{
					result = new MinimumLinkResult<TFit>(Array.Empty<int>(), Array.Empty<TFit>());
					return false;
				}
				AppendSegment(segmentKnots, segmentFits, knotCount2, intValues, tFits, ref outputKnotCount, ref outputFitCount);
			}
			Array.Resize(ref intValues, outputKnotCount);
			Array.Resize(ref tFits, outputFitCount);
			result = new MinimumLinkResult<TFit>(intValues, tFits);
			return true;
		}
		}
	}

	public static bool TryFindShared<TFit>(int pointCount, int seriesCount, TryFitMinimumLinkSeriesSegment<TFit> tryFit, out MinimumLinkMultiSeriesResult<TFit> result)
	{
		return TryFindShared(pointCount, seriesCount, ReadOnlySpan<int>.Empty, tryFit, out result);
	}

	public static bool TryFindShared<TFit>(int pointCount, int seriesCount, ReadOnlySpan<int> requiredKnotIndexes, TryFitMinimumLinkSeriesSegment<TFit> tryFit, out MinimumLinkMultiSeriesResult<TFit> result)
	{
		ValidateSharedInputs(pointCount, seriesCount, requiredKnotIndexes, tryFit);
		switch (pointCount)
		{
		case 0:
			result = new MinimumLinkMultiSeriesResult<TFit>(Array.Empty<int>(), CreateRows<TFit>(seriesCount, 0));
			return true;
		case 1:
			result = new MinimumLinkMultiSeriesResult<TFit>(new int[1], CreateRows<TFit>(seriesCount, 0));
			return true;
		default:
		{
			int[] linkCounts = new int[pointCount];
			int[] predecessors = new int[pointCount];
			TFit[][] predecessorFits = CreateRows<TFit>(seriesCount, pointCount);
			TFit[] candidateFits = new TFit[seriesCount];
			int[] segmentKnots = new int[pointCount];
			TFit[][] segmentFits = CreateRows<TFit>(seriesCount, pointCount - 1);
			int[] intValues = new int[pointCount];
			TFit[][] tFits = CreateRows<TFit>(seriesCount, pointCount - 1);
			int outputKnotCount = 0;
			int outputFitCount = 0;
			int requiredKnotIndex = 0;
			for (int i = 0; i < requiredKnotIndexes.Length; i++)
			{
				int requiredKnotIndex2 = requiredKnotIndexes[i];
				if (requiredKnotIndex2 != requiredKnotIndex)
				{
					if (!TrySolveSharedSegment(requiredKnotIndex, requiredKnotIndex2, seriesCount, tryFit, linkCounts, predecessors, predecessorFits, candidateFits, segmentKnots, segmentFits, out var knotCount))
					{
						result = new MinimumLinkMultiSeriesResult<TFit>(Array.Empty<int>(), CreateRows<TFit>(seriesCount, 0));
						return false;
					}
					AppendSharedSegment(segmentKnots, segmentFits, knotCount, intValues, tFits, ref outputKnotCount, ref outputFitCount);
					requiredKnotIndex = requiredKnotIndex2;
				}
			}
			if (requiredKnotIndex < pointCount - 1)
			{
				if (!TrySolveSharedSegment(requiredKnotIndex, pointCount - 1, seriesCount, tryFit, linkCounts, predecessors, predecessorFits, candidateFits, segmentKnots, segmentFits, out var knotCount2))
				{
					result = new MinimumLinkMultiSeriesResult<TFit>(Array.Empty<int>(), CreateRows<TFit>(seriesCount, 0));
					return false;
				}
				AppendSharedSegment(segmentKnots, segmentFits, knotCount2, intValues, tFits, ref outputKnotCount, ref outputFitCount);
			}
			Array.Resize(ref intValues, outputKnotCount);
			for (int j = 0; j < seriesCount; j++)
			{
				Array.Resize(ref tFits[j], outputFitCount);
			}
			result = new MinimumLinkMultiSeriesResult<TFit>(intValues, tFits);
			return true;
		}
		}
	}

	private static bool TrySolveSegment<TFit>(int start, int end, TryFitMinimumLinkSegment<TFit> tryFit, int[] linkCounts, int[] predecessors, TFit[] predecessorFits, int[] segmentKnots, TFit[] segmentFits, out int knotCount)
	{
		InitializePath(start, end, linkCounts, predecessors);
		for (int i = start; i < end; i++)
		{
			if (linkCounts[i] == int.MaxValue)
			{
				continue;
			}
			for (int j = i + 1; j <= end; j++)
			{
				if (tryFit(i, j, out var fit))
				{
					int linkCounts2 = linkCounts[i] + 1;
					if (ShouldReplacePath(linkCounts2, i, linkCounts[j], predecessors[j]))
					{
						linkCounts[j] = linkCounts2;
						predecessors[j] = i;
						predecessorFits[j] = fit;
					}
				}
			}
		}
		if (linkCounts[end] == int.MaxValue)
		{
			knotCount = 0;
			return false;
		}
		knotCount = linkCounts[end] + 1;
		int end2 = end;
		for (int knotCountOffset = knotCount - 1; knotCountOffset > 0; knotCountOffset--)
		{
			segmentKnots[knotCountOffset] = end2;
			segmentFits[knotCountOffset - 1] = predecessorFits[end2];
			end2 = predecessors[end2];
		}
		segmentKnots[0] = start;
		return true;
	}

	private static bool TrySolveSharedSegment<TFit>(int start, int end, int seriesCount, TryFitMinimumLinkSeriesSegment<TFit> tryFit, int[] linkCounts, int[] predecessors, TFit[][] predecessorFits, TFit[] candidateFits, int[] segmentKnots, TFit[][] segmentFits, out int knotCount)
	{
		InitializePath(start, end, linkCounts, predecessors);
		for (int i = start; i < end; i++)
		{
			if (linkCounts[i] == int.MaxValue)
			{
				continue;
			}
			for (int j = i + 1; j <= end; j++)
			{
				bool condition = true;
				for (int k = 0; k < seriesCount; k++)
				{
					if (!tryFit(k, i, j, out candidateFits[k]))
					{
						condition = false;
						break;
					}
				}
				if (!condition)
				{
					continue;
				}
				int linkCounts2 = linkCounts[i] + 1;
				if (ShouldReplacePath(linkCounts2, i, linkCounts[j], predecessors[j]))
				{
					linkCounts[j] = linkCounts2;
					predecessors[j] = i;
					for (int l = 0; l < seriesCount; l++)
					{
						predecessorFits[l][j] = candidateFits[l];
					}
				}
			}
		}
		if (linkCounts[end] == int.MaxValue)
		{
			knotCount = 0;
			return false;
		}
		knotCount = linkCounts[end] + 1;
		int end2 = end;
		for (int knotCountOffset = knotCount - 1; knotCountOffset > 0; knotCountOffset--)
		{
			segmentKnots[knotCountOffset] = end2;
			for (int m = 0; m < seriesCount; m++)
			{
				segmentFits[m][knotCountOffset - 1] = predecessorFits[m][end2];
			}
			end2 = predecessors[end2];
		}
		segmentKnots[0] = start;
		return true;
	}

	internal static void InitializePath(int start, int end, int[] linkCounts, int[] predecessors)
	{
		for (int i = start; i <= end; i++)
		{
			linkCounts[i] = int.MaxValue;
			predecessors[i] = -1;
		}
		linkCounts[start] = 0;
	}

	internal static bool ShouldReplacePath(int candidateLinkCount, int candidatePredecessor, int currentLinkCount, int currentPredecessor)
	{
		if (candidateLinkCount >= currentLinkCount)
		{
			if (candidateLinkCount == currentLinkCount)
			{
				return candidatePredecessor < currentPredecessor;
			}
			return false;
		}
		return true;
	}

	private static void AppendSegment<TFit>(int[] segmentKnots, TFit[] segmentFits, int segmentKnotCount, int[] knots, TFit[] fits, ref int outputKnotCount, ref int outputFitCount)
	{
		for (int i = ((outputKnotCount != 0) ? 1 : 0); i < segmentKnotCount; i++)
		{
			knots[outputKnotCount++] = segmentKnots[i];
		}
		for (int j = 0; j < segmentKnotCount - 1; j++)
		{
			fits[outputFitCount++] = segmentFits[j];
		}
	}

	private static void AppendSharedSegment<TFit>(int[] segmentKnots, TFit[][] segmentFits, int segmentKnotCount, int[] knots, TFit[][] fits, ref int outputKnotCount, ref int outputFitCount)
	{
		for (int i = ((outputKnotCount != 0) ? 1 : 0); i < segmentKnotCount; i++)
		{
			knots[outputKnotCount++] = segmentKnots[i];
		}
		for (int j = 0; j < segmentKnotCount - 1; j++)
		{
			for (int k = 0; k < segmentFits.Length; k++)
			{
				fits[k][outputFitCount] = segmentFits[k][j];
			}
			outputFitCount++;
		}
	}

	private static void ValidateInputs<TFit>(int pointCount, ReadOnlySpan<int> requiredKnotIndexes, TryFitMinimumLinkSegment<TFit> tryFit)
	{
		if (pointCount < 0)
		{
			throw new ArgumentOutOfRangeException("pointCount");
		}
		if (tryFit == null)
		{
			throw new ArgumentNullException("tryFit");
		}
		int requiredKnotIndexTerm = -1;
		for (int i = 0; i < requiredKnotIndexes.Length; i++)
		{
			int requiredKnotIndex = requiredKnotIndexes[i];
			if (requiredKnotIndex < 0 || requiredKnotIndex >= pointCount)
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

	private static void ValidateSharedInputs<TFit>(int pointCount, int seriesCount, ReadOnlySpan<int> requiredKnotIndexes, TryFitMinimumLinkSeriesSegment<TFit> tryFit)
	{
		if (pointCount < 0)
		{
			throw new ArgumentOutOfRangeException("pointCount");
		}
		if (seriesCount <= 0)
		{
			throw new ArgumentOutOfRangeException("seriesCount");
		}
		if (tryFit == null)
		{
			throw new ArgumentNullException("tryFit");
		}
		int requiredKnotIndexTerm = -1;
		for (int i = 0; i < requiredKnotIndexes.Length; i++)
		{
			int requiredKnotIndex = requiredKnotIndexes[i];
			if (requiredKnotIndex < 0 || requiredKnotIndex >= pointCount)
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

	private static T[][] CreateRows<T>(int rowCount, int columnCount)
	{
		T[][] rows = new T[rowCount][];
		for (int i = 0; i < rowCount; i++)
		{
			rows[i] = new T[columnCount];
		}
		return rows;
	}
}
