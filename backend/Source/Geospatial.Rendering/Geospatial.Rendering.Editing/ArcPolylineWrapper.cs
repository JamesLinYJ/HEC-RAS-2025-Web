using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Geospatial.DataStructures;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public struct ArcPolylineWrapper : IPointCollection, IVectorCollection<Point>, IReadOnlyVectorCollection<Point>, IEnumerable<Point>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Point>, IReadOnlyCollection<Point>, IReadOnlyPointCollection, ISpatiallyIndexedPointCollection
{
	public struct ArcPolylineWrapperEnumerator(ArcPolylineWrapper line) : IEnumerator<Point>, IEnumerator, IDisposable
	{
		private ArcPolylineWrapper _wrapper = line;

		private int _index = -1;

		public Point Current => _wrapper[_index];

		object IEnumerator.Current => Current;

		public void Dispose()
		{
		}

		public bool MoveNext()
		{
			if (_index == _wrapper.Count - 1)
			{
				return false;
			}
			_index++;
			return true;
		}

		public void Reset()
		{
			_index = 0;
		}
	}

	public Any<BaseTopology> NetworkAny;

	public int ArcFID;

	public BaseTopology Network => NetworkAny.Value;

	public BaseArc Arc => Network.Arcs[ArcFID];

	public bool HasEndNode => Arc.EndNode >= 0;

	public Point this[int index]
	{
		get
		{
			if (index < 0 || index >= Count)
			{
				throw new IndexOutOfRangeException();
			}
			int startNode = Arc.StartNode;
			if (startNode < 0)
			{
				throw new InvalidOperationException("Can not wrap an arc with no start node.");
			}
			if (index == 0)
			{
				return Network.Nodes[startNode].Point;
			}
			if (index == Count - 1 && HasEndNode)
			{
				return Network.Nodes[Arc.EndNode].Point;
			}
			return Arc.InternalPoints[index - 1];
		}
		set
		{
			if (index == 0 || (index == Count - 1 && HasEndNode))
			{
				throw new InvalidOperationException("Can not set node points for a wrapper collection");
			}
			Arc.InternalPoints[index - 1] = value;
		}
	}

	public bool IsReadOnly => false;

	public long VersionXY => Arc.VersionXY;

	public Extent Extent => Network.GetArcPolyline(ArcFID).Extent;

	public int Count => 1 + Arc.InternalPointCount + (HasEndNode ? 1 : 0);

	public ReaderWriterLock Lock
	{
		get
		{
			throw new InvalidOperationException("Lock not supported on wrapper collection.");
		}
	}

	public SpatialIndex<int> PointSpatialIndex => Network.GetArcPolyline(ArcFID).PointSpatialIndex;

	public ArcPolylineWrapper(Any<BaseTopology> network, int arcFid)
	{
		NetworkAny = network;
		ArcFID = arcFid;
	}

	public void Add(Point point)
	{
		if (HasEndNode)
		{
			throw new InvalidOperationException("Can not append points when the arc has an end node.");
		}
		BaseArc arc = Arc;
		if (arc.InternalPoints == null)
		{
			PointCollection pointCollection = (arc.InternalPoints = new PointCollection());
		}
		Arc.InternalPoints.Add(point);
	}

	public void Insert(int index, Point point)
	{
		if (index == Count && HasEndNode)
		{
			throw new InvalidOperationException("Can not append points when the arc has an end node.");
		}
		if (index == 0)
		{
			throw new InvalidOperationException("Can not insert at start node position.");
		}
		BaseArc arc = Arc;
		if (arc.InternalPoints == null)
		{
			PointCollection pointCollection = (arc.InternalPoints = new PointCollection());
		}
		Arc.InternalPoints.Insert(index - 1, point);
	}

	public void RemoveAt(int index)
	{
		if (index == 0 || (index == Count - 1 && HasEndNode))
		{
			throw new InvalidOperationException("Can not remove a node on a wrapper collection.");
		}
		Arc.InternalPoints.RemoveAt(index - 1);
	}

	public void ThawOne()
	{
		Arc.ThawOne();
	}

	public void FreezeOne()
	{
		Arc.FreezeOne();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public IEnumerator<Point> GetEnumerator()
	{
		return new ArcPolylineWrapperEnumerator(this);
	}

	public void Clear()
	{
		throw new InvalidOperationException("Clear not supported on wrapper collection.");
	}
}
