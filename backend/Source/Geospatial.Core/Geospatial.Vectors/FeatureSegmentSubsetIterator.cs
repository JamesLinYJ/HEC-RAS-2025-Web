using System;
using System.Collections;
using System.Collections.Generic;
using Geospatial.Rasters;

namespace Geospatial.Vectors;

public class FeatureSegmentSubsetIterator : IEnumerator<int>, IEnumerator, IDisposable
{
	private FeatureSegmentSubset _featureSegmentSubset;

	private int _currentRange = -1;

	private int _currentFeature = -1;

	public int Current
	{
		get
		{
			if (_currentRange < 0 || _currentRange >= _featureSegmentSubset.Ranges.Count)
				throw new InvalidOperationException("The segment iterator is not positioned on a value.");
			return _currentFeature;
		}
	}

	object IEnumerator.Current => ((IEnumerator<int>)this).Current;

	public FeatureSegmentSubsetIterator(FeatureSegmentSubset featureSegmentSubset)
	{
		_featureSegmentSubset = featureSegmentSubset;
	}

	public bool MoveNext()
	{
		if (_currentRange >= _featureSegmentSubset.Ranges.Count) return false;
		if (_currentRange >= 0 && _currentFeature < _featureSegmentSubset.Ranges[_currentRange].End)
		{
			_currentFeature++;
			return true;
		}
		while (++_currentRange < _featureSegmentSubset.Ranges.Count)
		{
			Range1D range = _featureSegmentSubset.Ranges[_currentRange];
			if (range.IsNull() || range.Start > range.End) continue;
			_currentFeature = range.Start;
			return true;
		}
		return false;
	}

	public void Reset()
	{
		_currentRange = -1;
		_currentFeature = -1;
	}

	public void Dispose()
	{
	}
}
