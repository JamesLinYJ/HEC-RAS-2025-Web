using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Geospatial.DataStructures;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Geospatial.Vectors;

public class ComplexPolygonCollection : VectorCollectionBase<ComplexPolygon>, IReadOnlyComplexPolygonCollection, IReadOnlyVectorCollection<ComplexPolygon>, IEnumerable<ComplexPolygon>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<ComplexPolygon>, IReadOnlyCollection<ComplexPolygon>
{
	private class VirtualSegmentCollection : IReadOnlyList<Segment>, IEnumerable<Segment>, IEnumerable, IReadOnlyCollection<Segment>
	{
		private ComplexPolygonCollection _inputs;

		public int Count => _inputs.Polygons.Sum((ComplexPolygon a) => a.SegmentCount);

		public Segment this[int index]
		{
			get
			{
				int intervalIndex = 0;
				int count = Count;
				for (int i = 0; i < count; i++)
				{
					ComplexPolygon complexPolygon = _inputs[i];
					int segmentCountComplexPolygonTerm = intervalIndex + complexPolygon.SegmentCount;
					if (segmentCountComplexPolygonTerm > index)
					{
						return complexPolygon.Segments[index - intervalIndex];
					}
					intervalIndex = segmentCountComplexPolygonTerm;
				}
				throw new ArgumentOutOfRangeException();
			}
		}

		public VirtualSegmentCollection(ComplexPolygonCollection inputs)
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
			foreach (ComplexPolygon input in _inputs)
			{
				foreach (Polygon ring in input.Rings)
				{
					foreach (Segment segment in ring.Segments)
					{
						yield return segment;
					}
				}
			}
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("ComplexPolygonCollection");

	public Versioned<List<Polygon>> _allRingsCache = new Versioned<List<Polygon>>();

	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	public IReadOnlyList<ComplexPolygon> Polygons => _elements;

	public IReadOnlyList<Segment> Segments { get; private set; }

	public List<Polygon> FlattenedRingsList => _allRingsCache.Get(VersionXY, this, (ComplexPolygonCollection col) => col.Polygons.SelectMany((ComplexPolygon pg) => pg.Rings).ToList());

	public ComplexPolygonCollection()
	{
	}

	public ComplexPolygonCollection(int capacity)
		: base(capacity)
	{
	}

	public ComplexPolygonCollection(IEnumerable<ComplexPolygon> polygons)
		: base(polygons)
	{
	}

	public ComplexPolygonCollection(IList<ComplexPolygon> polygons)
		: base((IEnumerable<ComplexPolygon>)polygons)
	{
	}

	protected override void Init()
	{
		Segments = new VirtualSegmentCollection(this);
	}

	public static ComplexPolygonCollection FromDisjointRings(IReadOnlyList<Polygon> rings)
	{
		ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection();
		int count = rings.Count;
		if (count == 0)
		{
			return complexPolygonCollection;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		Extent[] extents = new Extent[count];
		for (int i = 0; i < count; i++)
		{
			extents[i] = rings[i].Extent;
		}
		int[] order = Polygon.StableSort(rings);
		int[] orderPos = new int[count];
		for (int j = 0; j < count; j++)
		{
			orderPos[order[j]] = j;
		}
		IPointInPolygonChecker[] checkers = new IPointInPolygonChecker[count];
		System.Threading.Tasks.Parallel.For(0, count, (int ordinal) =>
		{
			checkers[ordinal] = rings[ordinal].GetBestPointInPolygonChecker();
		});
		int[] parent = new int[count];
		System.Threading.Tasks.Parallel.For(0, count, (int ordinal2) =>
		{
			parent[ordinal2] = -1;
			Point point = rings[ordinal2][0];
			for (int orderPosTerm = orderPos[ordinal2] - 1; orderPosTerm >= 0; orderPosTerm--)
			{
				int order2 = order[orderPosTerm];
				if (extents[order2].Contains(point) && checkers[order2].Contains(point))
				{
					parent[ordinal2] = order2;
					break;
				}
			}
		});
		int[] intValues = new int[count];
		int[] intValuesArray = new int[count];
		ComplexPolygon[] complexPolygons = new ComplexPolygon[count];
		int[] orderArray = order;
		foreach (int order3 in orderArray)
		{
			Polygon polygon = rings[order3];
			if (parent[order3] == -1)
			{
				intValues[order3] = 0;
				intValuesArray[order3] = order3;
				if (polygon.Area < 0.0)
				{
					polygon.Reverse();
				}
				complexPolygons[order3] = ComplexPolygon.FromOuterRing(polygon);
				complexPolygonCollection.Add(complexPolygons[order3]);
			}
			else
			{
				intValues[order3] = intValues[parent[order3]] + 1;
				intValuesArray[order3] = intValuesArray[parent[order3]];
				if ((intValues[order3] & 1) == 0 != polygon.Area > 0.0)
				{
					polygon.Reverse();
				}
				complexPolygons[intValuesArray[order3]].AddWithoutValidation(polygon);
			}
		}
		if (stopwatch.ElapsedMilliseconds > 1000)
		{
			_logger.LogDebug("Nested {Rings} disjoint rings in {Ms} ms -> {Polygons} polygons", count, stopwatch.ElapsedMilliseconds, complexPolygonCollection.Count);
		}
		return complexPolygonCollection;
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _spIdxCache.Get(VersionXY, CreateAreaSpatialIndex);
	}

	private SpatialIndex<int> CreateAreaSpatialIndex()
	{
		return SpatialIndex.FromComplexPolygons(_elements);
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, CreateSegmentSpatialIndex);
	}

	private SpatialIndex<FeatureSegmentSubset> CreateSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex(FlattenedRingsList);
	}

	public double ComputeArea()
	{
		double areaSum = 0.0;
		for (int i = 0; i < _elements.Count; i++)
		{
			areaSum += _elements[i].ComputeArea();
		}
		return areaSum;
	}

	public void Translate(double x, double y)
	{
		for (int i = 0; i < _elements.Count; i++)
		{
			_elements[i].Translate(x, y);
		}
	}
}
