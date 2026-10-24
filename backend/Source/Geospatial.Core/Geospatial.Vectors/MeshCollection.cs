using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Utility;

namespace Geospatial.Vectors;

public class MeshCollection : VectorCollectionBase<Mesh>, IMeshCollection, IVectorCollection<Mesh>, IReadOnlyVectorCollection<Mesh>, IEnumerable<Mesh>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Mesh>, IReadOnlyCollection<Mesh>
{
	public class VirtualSegmentCollection : IReadOnlyList<Segment>, IEnumerable<Segment>, IEnumerable, IReadOnlyCollection<Segment>
	{
		private IList<Mesh> _inputs;

		public int Count => _inputs.Sum((Mesh a) => a.Perimeter.SegmentCount);

		public Segment this[int index]
		{
			get
			{
				int intervalIndex = 0;
				int count = Count;
				for (int i = 0; i < count; i++)
				{
					Mesh mesh = _inputs[i];
					int segmentCountPerimeterTerm = intervalIndex + mesh.Perimeter.SegmentCount;
					if (segmentCountPerimeterTerm > index)
					{
						return mesh.Perimeter.Segments[index - intervalIndex];
					}
					intervalIndex = segmentCountPerimeterTerm;
				}
				throw new ArgumentOutOfRangeException();
			}
		}

		public VirtualSegmentCollection(IList<Mesh> inputs)
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
			foreach (Mesh input in _inputs)
			{
				foreach (Segment segment in input.Perimeter.Segments)
				{
					yield return segment;
				}
			}
		}
	}

	private readonly Versioned<double> _areaCache = new Versioned<double>();

	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	public IReadOnlyList<Segment> Segments { get; private set; }

	public double TotalArea => _areaCache.Get(VersionXY, ComputeArea);

	public MeshCollection()
	{
	}

	public MeshCollection(IEnumerable<Mesh> polygons)
		: base(polygons)
	{
	}

	protected override void Init()
	{
		Segments = new VirtualSegmentCollection(_elements);
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, CreateSegmentSpatialIndex);
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _spIdxCache.Get(VersionXY, CreateAreaSpatialIndex);
	}

	private SpatialIndex<FeatureSegmentSubset> CreateSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex(_elements.Select((Mesh a) => a.Perimeter).ToList());
	}

	private SpatialIndex<int> CreateAreaSpatialIndex()
	{
		return SpatialIndex.FromMeshes(_elements);
	}

	private double ComputeArea()
	{
		return _elements.Sum((Mesh pg) => pg.Perimeter.Area);
	}
}
