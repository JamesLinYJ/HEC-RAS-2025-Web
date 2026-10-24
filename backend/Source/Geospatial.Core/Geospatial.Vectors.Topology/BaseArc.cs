using System;
using System.Collections.Generic;
using System.IO;
using Utility.Extensions;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

public class BaseArc : ISpatialVersion, IStreamableObject
{
	protected int _startNode;

	protected int _endNode;

	public long ExternallyTracked_DateOfGenesis;

	private CollectionVersionTracker _tracker;

	private PointCollection _internalPoints;

	public int StartNode
	{
		get
		{
			return _startNode;
		}
		set
		{
			if (_startNode != value)
			{
				_tracker.OnDeleted();
				_startNode = value;
			}
		}
	}

	public int EndNode
	{
		get
		{
			return _endNode;
		}
		set
		{
			if (_endNode != value)
			{
				_tracker.OnDeleted();
				_endNode = value;
			}
		}
	}

	public int InternalPointCount => InternalPoints?.Count ?? 0;

	public PointCollection InternalPoints
	{
		get
		{
			return _internalPoints;
		}
		set
		{
			if (_internalPoints != null)
			{
				_tracker.OnDeleted(_internalPoints);
			}
			else
			{
				_tracker.OnDeleted();
			}
			_internalPoints = value;
		}
	}

	public long VersionXY => _tracker.GetVersion(in _internalPoints);

	public BaseArc()
	{
	}

	public BaseArc(int startNode, int endNode, List<Point> internalPoints)
	{
		_tracker = new CollectionVersionTracker();
		StartNode = startNode;
		EndNode = endNode;
		if (!internalPoints.IsNullOrEmpty())
		{
			InternalPoints = new PointCollection(internalPoints);
		}
	}

	public void AddPoints(IList<Point> pts)
	{
		if (InternalPoints == null)
		{
			InternalPoints = new PointCollection(pts);
		}
		else
		{
			InternalPoints.AddRange(pts);
		}
	}

	public void AddPoints(Span<Point> pts)
	{
		if (InternalPoints == null)
		{
			InternalPoints = new PointCollection(pts.Length);
		}
		InternalPoints.AddRange(pts);
	}

	public void AddPoint(Point pt)
	{
		if (InternalPoints == null)
		{
			InternalPoints = new PointCollection { pt };
		}
		else
		{
			InternalPoints.Add(pt);
		}
	}

	public void InsertPoints(IList<Point> pts, int index)
	{
		if (InternalPoints == null)
		{
			InternalPoints = new PointCollection(pts);
			return;
		}
		for (int i = 0; i < pts.Count; i++)
		{
			InternalPoints.Insert(index + i, pts[i]);
		}
	}

	public void InsertPoint(int index, Point pt)
	{
		if (InternalPoints == null)
		{
			InternalPoints = new PointCollection { pt };
		}
		else
		{
			InternalPoints.Insert(index, pt);
		}
	}

	public void SetPoints(IList<Point> pts)
	{
		InternalPoints = new PointCollection(pts);
	}

	public int OtherNode(int node)
	{
		if (node == StartNode)
		{
			return EndNode;
		}
		if (node == EndNode)
		{
			return StartNode;
		}
		throw new ArgumentException();
	}

	public string DebugDisplay()
	{
		return StartNode + "->" + EndNode + ", " + (InternalPoints?.Count ?? 0) + " points";
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}

	public virtual void StreamOut(BinaryWriter bw)
	{
		bw.Write(StartNode);
		bw.Write(EndNode);
		bw.Write(InternalPoints != null);
		if (InternalPoints != null)
		{
			InternalPoints.StreamOut(bw);
		}
	}

	public virtual void StreamIn(BinaryReader br)
	{
		_startNode = br.ReadInt32();
		_endNode = br.ReadInt32();
		if (br.ReadBoolean())
		{
			PointCollection pointCollection = new PointCollection();
			pointCollection.StreamIn(br);
			InternalPoints = pointCollection;
		}
	}
}
