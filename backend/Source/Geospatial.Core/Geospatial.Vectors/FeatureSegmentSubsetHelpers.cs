using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Geospatial.Rasters;
using Utility.DataStructures;

namespace Geospatial.Vectors;

public static class FeatureSegmentSubsetHelpers
{
	public static bool TryMakeExcluding(int fid, int segmentCount, Range1D gap, out FeatureSegmentSubset subset)
	{
		if (gap.IsNull())
		{
			subset = new FeatureSegmentSubset(0, segmentCount - 1)
			{
				Fid = fid
			};
			return true;
		}
		bool startGapTerm = gap.Start > 0;
		bool endGapTerm = gap.End < segmentCount - 1;
		if (startGapTerm)
		{
			subset = new FeatureSegmentSubset(0, gap.Start - 1)
			{
				Fid = fid
			};
			if (endGapTerm)
			{
				subset.Ranges.Add(new Range1D(gap.End + 1, segmentCount - 1));
			}
			return true;
		}
		if (endGapTerm)
		{
			subset = new FeatureSegmentSubset(gap.End + 1, segmentCount - 1)
			{
				Fid = fid
			};
			return true;
		}
		subset = default;
		return false;
	}

	public static void Dedupe(this List<FeatureSegmentSubset> subsets)
	{
		if (subsets.Count == 0)
		{
			return;
		}
		FeatureSegmentSubset subset = subsets[0];
		int start = subset.Ranges[0].Start;
		ref CompactList<Range1D> ranges = ref subset.Ranges;
		Range1D range1D = new Range1D(start, ranges[ranges.Count - 1].End);
		int ordinal = 0;
		for (int i = 1; i < subsets.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = subsets[i];
			int start2 = featureSegmentSubset.Ranges[0].Start;
			ref CompactList<Range1D> ranges2 = ref featureSegmentSubset.Ranges;
			Range1D range1D2 = new Range1D(start2, ranges2[ranges2.Count - 1].End);
			if (subset.Fid != featureSegmentSubset.Fid || !range1D.IsConesecutiveOrOverlappingWith(range1D2))
			{
				subsets[ordinal] = subset;
				ordinal++;
				subset = featureSegmentSubset;
				range1D = range1D2;
			}
			else
			{
				subset = subset.Merge(featureSegmentSubset);
			}
		}
		subsets[ordinal] = subset;
		if (ordinal != subsets.Count - 1)
		{
			subsets.RemoveRange(ordinal + 1, subsets.Count - 1 - ordinal);
		}
	}

	public static IEnumerable<int> IterateAllCandidates(this List<FeatureSegmentSubset> subsets)
	{
		foreach (FeatureSegmentSubset subset in subsets)
		{
			foreach (int subset2 in subset)
			{
				yield return subset2;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void IterateAllCandidates<T>(this IList<FeatureSegmentSubset> subsets, T model, Action<T, int, int> executeCandidate)
	{
		for (int i = 0; i < subsets.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = subsets[i];
			for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
			{
				Range1D range1D = featureSegmentSubset.Ranges[j];
				for (int k = range1D.Start; k <= range1D.End; k++)
				{
					executeCandidate(model, featureSegmentSubset.Fid, k);
				}
			}
		}
	}
}
