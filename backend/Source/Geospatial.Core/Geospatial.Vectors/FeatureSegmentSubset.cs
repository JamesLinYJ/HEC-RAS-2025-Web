using System;
using System.Collections;
using System.Collections.Generic;
using Geospatial.Rasters;
using Utility.DataStructures;

namespace Geospatial.Vectors;

public struct FeatureSegmentSubset : IEnumerable<int>, IEnumerable
{
	public int Fid = 0;

	public CompactList<Range1D> Ranges = default;

	public FeatureSegmentSubset()
	{
	}

	public FeatureSegmentSubset(int initialRangeStart, int initialRangeEnd)
	{
		Ranges.Add(new Range1D(initialRangeStart, initialRangeEnd));
	}

	public FeatureSegmentSubset Merge(FeatureSegmentSubset other)
	{
		if (other.Fid != Fid)
		{
			throw new ArgumentException();
		}
		FeatureSegmentSubset result = this;
		result.Ranges = Range1D.Merge(Ranges, other.Ranges);
		return result;
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return new FeatureSegmentSubsetIterator(this);
	}

	public IEnumerator<int> GetEnumerator()
	{
		return new FeatureSegmentSubsetIterator(this);
	}
}
