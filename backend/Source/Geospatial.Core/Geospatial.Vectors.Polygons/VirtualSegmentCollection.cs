using System;
using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors.Polygons;

internal class VirtualSegmentCollection : IReadOnlyList<Segment>, IEnumerable<Segment>, IEnumerable, IReadOnlyCollection<Segment>
{
	private IList<Polygon> _inputs;

	public int Count
	{
		get
		{
			int segmentCountSum = 0;
			for (int i = 0; i < _inputs.Count; i++)
			{
				segmentCountSum += _inputs[i].SegmentCount;
			}
			return segmentCountSum;
		}
	}

	public Segment this[int index]
	{
		get
		{
			int intervalIndex = 0;
			int count = Count;
			for (int i = 0; i < count; i++)
			{
				Polygon polygon = _inputs[i];
				int segmentCountPolygonTerm = intervalIndex + polygon.SegmentCount;
				if (segmentCountPolygonTerm > index)
				{
					return polygon.Segments[index - intervalIndex];
				}
				intervalIndex = segmentCountPolygonTerm;
			}
			throw new ArgumentOutOfRangeException();
		}
	}

	public VirtualSegmentCollection(IList<Polygon> inputs)
	{
		_inputs = inputs ?? throw new ArgumentNullException("inputs");
	}

	public IEnumerator<Segment> GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator_Internal();
	}

	private IEnumerator<Segment> GetEnumerator_Internal()
	{
		foreach (Polygon input in _inputs)
		{
			foreach (Segment segment in input.Segments)
			{
				yield return segment;
			}
		}
	}
}
