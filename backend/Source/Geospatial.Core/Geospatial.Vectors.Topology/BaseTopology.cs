using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Geospatial.DataStructures;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.Memory;
using Utility.ORM;
using Utility.Systems;

namespace Geospatial.Vectors.Topology;

public abstract class BaseTopology : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("Topology");

	protected readonly List<Node> _nodes;

	private readonly IReadOnlyList<BaseArc> _arcsView;

	protected Polyline[] _arcPolylineCache;

	protected long _nodeVersion = 1L;

	protected long _lastCachedPolylineVersion = -1L;

	protected CollectionVersionTracker _arcTracker;

	protected long _globalVersion;

	private Freezer _freezer;

	private readonly Versioned<Extent> _extVersion = new Versioned<Extent>();

	private readonly Versioned<SpatialIndex<int>> _nodeSpIdxCache = new Versioned<SpatialIndex<int>>();

	private readonly Versioned<SpatialIndex<Point>> _nodePointsSpIdxCache = new Versioned<SpatialIndex<Point>>();

	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _arcSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	public int NodeCount => Nodes.Count;

	public int ArcCount => Arcs.Count;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			if (!Lock.TryEnterReadLock(-1))
			{
				throw new InvalidOperationException("Somehow, with infinite timeout, we couldn't acquire the read lock?...");
			}
			try
			{
				return GetVersionInternal();
			}
			catch
			{
				throw;
			}
			finally
			{
				Lock.ExitReadLock();
			}
		}
	}

	public Extent Extent => _extVersion.Get(VersionXY, ComputeExtent);

	public SpatialIndex<int> NodeSpatialIndex => _nodeSpIdxCache.Get(_nodeVersion, ComputeNodeSpatialIndex);

	public SpatialIndex<Point> NodePointsSpatialIndex => _nodePointsSpIdxCache.Get(_nodeVersion, ComputeNodePointsSpatialIndex);

	public SpatialIndex<FeatureSegmentSubset> ArcSpatialIndex => _arcSpIdxCache.Get(GetArcCacheVersion(), ComputeArcSpatialIndex);

	public IReadOnlyList<Node> Nodes => _nodes;

	public IReadOnlyList<BaseArc> Arcs => _arcsView;

	[DatasetName("Node Attributes")]
	public Table NodeMetadata { get; protected set; }

	[DatasetName("Arc Attributes")]
	public Table ArcMetadata { get; protected set; }

	public Table GlobalMetadata { get; protected set; }

	public ReaderWriterLockSlim Lock { get; } = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

	protected Extent SpatialIndexExtent
	{
		get
		{
			if (!Extent.IsNull())
			{
				return Extent.EnforceMinDimensions(0.01, 0.01).RatioAroundCenter(1.05);
			}
			return Extent.NullExtent;
		}
	}

	protected virtual long GetVersionInternal()
	{
		return _arcTracker.GetVersion(Arcs) + _nodeVersion + _globalVersion;
	}

	public void DebugIncrementGlobalVersion()
	{
		_globalVersion++;
	}

	public void FreezeOne()
	{
		_arcTracker.FreezeOne<BaseArc>(Arcs);
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_arcTracker.ThawOne();
		_freezer.ThawOne();
	}

	public long GetArcCacheVersion()
	{
		return _nodeVersion + _arcTracker.GetVersion(Arcs);
	}

	protected BaseTopology(IReadOnlyList<BaseArc> arcs)
	{
		_arcsView = arcs;
		_nodes = new List<Node>();
		NodeMetadata = new Table
		{
			InitValueTypesToDefault = true
		};
		ArcMetadata = new Table
		{
			InitValueTypesToDefault = true
		};
		GlobalMetadata = new Table
		{
			InitValueTypesToDefault = true
		};
		GlobalMetadata.AddNewRow();
	}

	protected abstract void RemoveArcAt(int arcFid);

	public abstract TableRow AddArc(int startNode, int endNode, List<Point> internalVertexes, out int fid);

	public TableRow AddNode(Point pt)
	{
		int fid;
		return AddNode(pt, out fid);
	}

	public TableRow AddNode(double x, double y)
	{
		int fid;
		return AddNode(new Point(x, y), out fid);
	}

	public TableRow AddNode(double x, double y, out int fid)
	{
		return AddNode(new Point(x, y), out fid);
	}

	public TableRow AddNode(Point pt, out int fid)
	{
		return AddNode(new Node(pt), out fid);
	}

	public TableRow AddNode(Node node, out int fid)
	{
		_nodes.Add(node);
		_nodeVersion++;
		return NodeMetadata.AddNewRow(out fid);
	}

	public void RemoveNode(int nodeId, bool liveUpdateTopology = true)
	{
		_nodes.RemoveAt(nodeId);
		NodeMetadata.RemoveRow(nodeId);
		if (liveUpdateTopology)
		{
			for (int i = 0; i < Arcs.Count; i++)
			{
				BaseArc baseArc = Arcs[i];
				if (baseArc.StartNode == nodeId)
				{
					baseArc.StartNode = -1;
				}
				else if (baseArc.StartNode > nodeId)
				{
					baseArc.StartNode--;
				}
				if (baseArc.EndNode == nodeId)
				{
					baseArc.EndNode = -1;
				}
				else if (baseArc.EndNode > nodeId)
				{
					baseArc.EndNode--;
				}
			}
		}
		_nodeVersion++;
	}

	public void RemoveNodes(IEnumerable<int> nodesToRemove)
	{
		int[] intValues = new int[Nodes.Count];
		intValues.Fill(1);
		foreach (int nodesToRemove2 in nodesToRemove)
		{
			intValues[nodesToRemove2] = 0;
		}
		RemoveNodesByMask(intValues);
	}

	protected void RemoveNodesByMask(int[] nodeData)
	{
		bool[] boolValues = new bool[nodeData.Length];
		for (int i = 0; i < nodeData.Length; i++)
		{
			boolValues[i] = nodeData[i] == 0;
		}
		for (int countOffset = Nodes.Count - 1; countOffset >= 0; countOffset--)
		{
			if (nodeData[countOffset] == 0)
			{
				RemoveNode(countOffset, liveUpdateTopology: false);
			}
		}
		int nodeData2 = 0;
		for (int j = 0; j < nodeData.Length; j++)
		{
			if (nodeData[j] == 0)
			{
				nodeData2++;
			}
			nodeData[j] = nodeData2;
		}
		for (int k = 0; k < Arcs.Count; k++)
		{
			BaseArc baseArc = Arcs[k];
			if (baseArc.StartNode != -1)
			{
				baseArc.StartNode = (boolValues[baseArc.StartNode] ? (-1) : (baseArc.StartNode - nodeData[baseArc.StartNode]));
			}
			if (baseArc.EndNode != -1)
			{
				baseArc.EndNode = (boolValues[baseArc.EndNode] ? (-1) : (baseArc.EndNode - nodeData[baseArc.EndNode]));
			}
		}
	}

	public TableRow InsertNode(int index, Point pt)
	{
		Node pt2 = new Node(pt);
		_nodes.Insert(index, pt2);
		_nodeVersion++;
		for (int i = 0; i < Arcs.Count; i++)
		{
			BaseArc baseArc = Arcs[i];
			if (baseArc.StartNode >= index)
			{
				baseArc.StartNode++;
			}
			if (baseArc.EndNode >= index)
			{
				baseArc.EndNode++;
			}
		}
		return NodeMetadata.InsertRow(index);
	}

	public void MoveNode(int nodeId, Point newLocation)
	{
		Nodes[nodeId].Point = newLocation;
		_nodeVersion++;
	}

	protected void AddUnsortedNodeArcConnection(int arcFid)
	{
		BaseArc baseArc = Arcs[arcFid];
		if (baseArc.StartNode < Nodes.Count && baseArc.StartNode != -1 && !Nodes[baseArc.StartNode].Arcs.Contains(new NodeArcConnection(arcFid, isStart: true)))
		{
			Nodes[baseArc.StartNode].Arcs.Add(new NodeArcConnection(arcFid, isStart: true));
		}
		if (baseArc.EndNode < Nodes.Count && baseArc.EndNode != -1 && !Nodes[baseArc.EndNode].Arcs.Contains(new NodeArcConnection(arcFid, isStart: false)))
		{
			Nodes[baseArc.EndNode].Arcs.Add(new NodeArcConnection(arcFid, isStart: false));
		}
	}

	protected void RemoveUnsortedNodeArcConnection(int arcFid)
	{
		BaseArc baseArc = Arcs[arcFid];
		if (baseArc.StartNode < Nodes.Count && baseArc.StartNode != -1)
		{
			Nodes[baseArc.StartNode].Arcs.Remove(new NodeArcConnection(arcFid, isStart: true));
		}
		if (baseArc.EndNode < Nodes.Count && baseArc.EndNode != -1)
		{
			Nodes[baseArc.EndNode].Arcs.Remove(new NodeArcConnection(arcFid, isStart: false));
		}
	}

	public bool NodesFormArc(int movingNode, int checkNode, out int sharedArcIndex)
	{
		foreach (NodeArcConnection arc in Nodes[movingNode].Arcs)
		{
			BaseArc baseArc = Arcs[arc.ArcID];
			if (baseArc.EndNode == checkNode || baseArc.StartNode == checkNode)
			{
				sharedArcIndex = arc.ArcID;
				return true;
			}
		}
		sharedArcIndex = -1;
		return false;
	}

	public TableRow AddArc(int startNode, int endNode, List<Point> internalVertexes)
	{
		int fid;
		return AddArc(startNode, endNode, internalVertexes, out fid);
	}

	public TableRow AddArc(int startNode, int endNode)
	{
		int fid;
		return AddArc(startNode, endNode, null, out fid);
	}

	public void UpdateArc(int arcFid, int newStartNode, int newEndNode, List<Point> newInternalVertices)
	{
		ReconnectArc(arcFid, newStartNode, isStart: true);
		ReconnectArc(arcFid, newEndNode, isStart: false);
		Arcs[arcFid].InternalPoints = ((newInternalVertices == null) ? null : new PointCollection(newInternalVertices));
	}

	public void RemoveArc(int arcFID, bool liveUpdateTopology = true)
	{
		BaseArc arc = Arcs[arcFID];
		if (liveUpdateTopology)
		{
			RemoveUnsortedNodeArcConnection(arcFID);
		}
		RemoveArcAt(arcFID);
		ArcMetadata.RemoveRow(arcFID);
		if (liveUpdateTopology)
		{
			foreach (Node node in _nodes)
			{
				if (node.Arcs == null)
				{
					continue;
				}
				for (int i = 0; i < node.Arcs.Count; i++)
				{
					NodeArcConnection nodeArcConnection = node.Arcs[i];
					if (nodeArcConnection.ArcID == arcFID)
					{
						node.Arcs.Remove(nodeArcConnection);
						i--;
					}
					else if (nodeArcConnection.ArcID > arcFID)
					{
						nodeArcConnection.ArcID--;
						node.Arcs[i] = nodeArcConnection;
					}
				}
			}
		}
		_arcTracker.OnDeleted(arc);
	}

	public void MergeArcs(int nodeFid, int keepArcFid, int deleteArcFid, bool keepArcNodeConnectionIsStart, bool removeArcNodeConnectionIsStart)
	{
		Node node = Nodes[nodeFid];
		BaseArc baseArc = Arcs[keepArcFid];
		BaseArc baseArc2 = Arcs[deleteArcFid];
		if (keepArcFid == deleteArcFid)
		{
			return;
		}
		if (keepArcNodeConnectionIsStart)
		{
			baseArc.InsertPoint(0, node.Point);
			if (baseArc2.InternalPointCount != 0)
			{
				if (removeArcNodeConnectionIsStart)
				{
					PointCollection pointCollection = baseArc2.InternalPoints.Clone();
					pointCollection.Reverse();
					baseArc.InsertPoints(pointCollection, 0);
				}
				else
				{
					baseArc.InsertPoints(baseArc2.InternalPoints, 0);
				}
			}
		}
		else
		{
			baseArc.AddPoint(node.Point);
			if (baseArc2.InternalPointCount != 0)
			{
				if (!removeArcNodeConnectionIsStart)
				{
					PointCollection pointCollection2 = baseArc2.InternalPoints.Clone();
					pointCollection2.Reverse();
					baseArc.AddPoints(pointCollection2);
				}
				else
				{
					baseArc.AddPoints(baseArc2.InternalPoints);
				}
			}
		}
		ReconnectArc(keepArcFid, removeArcNodeConnectionIsStart ? baseArc2.EndNode : baseArc2.StartNode, keepArcNodeConnectionIsStart);
	}

	public void MergeArcs(int nodeFid)
	{
		Node node = Nodes[nodeFid];
		if (node.Arcs.Count != 2 || node.Arcs[0].ArcID == node.Arcs[1].ArcID)
		{
			throw new InvalidOperationException("Can only merge a node when 2 unique arcs connect to it.");
		}
		NodeArcConnection nodeArcConnection = node.Arcs[0];
		NodeArcConnection nodeArcConnection2 = node.Arcs[1];
		int arcID = nodeArcConnection.ArcID;
		int arcID2 = nodeArcConnection2.ArcID;
		MergeArcs(nodeFid, arcID, arcID2, nodeArcConnection.IsStart, nodeArcConnection2.IsStart);
	}

	public void ReconnectArc(int arcId, int newNodeId, bool isStart)
	{
		BaseArc baseArc = Arcs[arcId];
		RemoveUnsortedNodeArcConnection(arcId);
		if (isStart)
		{
			baseArc.StartNode = newNodeId;
		}
		else
		{
			baseArc.EndNode = newNodeId;
		}
		AddUnsortedNodeArcConnection(arcId);
	}

	public bool ArcHasInternalPointIdenticalToStartOrEndNode(int arcIndex)
	{
		BaseArc baseArc = Arcs[arcIndex];
		if (baseArc.InternalPointCount == 0)
		{
			return false;
		}
		if (baseArc.StartNode != -1 && baseArc.InternalPoints[0].DistanceTo(Nodes[baseArc.StartNode].Point) == 0.0)
		{
			return true;
		}
		if (baseArc.EndNode != -1)
		{
			PointCollection internalPoints = baseArc.InternalPoints;
			if (internalPoints[internalPoints.Count - 1].DistanceTo(Nodes[baseArc.EndNode].Point) == 0.0)
			{
				return true;
			}
		}
		return false;
	}

	public void RebuildNodeToArcPointers()
	{
		for (int i = 0; i < Nodes.Count; i++)
		{
			Nodes[i].Arcs.Clear();
		}
		for (int j = 0; j < Arcs.Count; j++)
		{
			BaseArc baseArc = Arcs[j];
			if (baseArc.StartNode != -1 && baseArc.StartNode < Nodes.Count)
			{
				Nodes[baseArc.StartNode].Arcs.Add(new NodeArcConnection(j, isStart: true));
			}
			if (baseArc.EndNode != -1 && baseArc.EndNode < Nodes.Count)
			{
				Nodes[baseArc.EndNode].Arcs.Add(new NodeArcConnection(j, isStart: false));
			}
		}
	}

	public void SetArcMetadataTable(Table tbl)
	{
		ArcMetadata = tbl;
	}

	public void SetNodeMetadataTable(Table tbl)
	{
		NodeMetadata = tbl;
	}

	public bool HitTestNode(Point queryPt, double hitTestRadius, out int elementIndex)
	{
		elementIndex = -1;
		if (Nodes.Count == 0)
		{
			return false;
		}
		SpatialIndex<int> nodeSpatialIndex = NodeSpatialIndex;
		Extent extent = Extent.CenteredOn(queryPt, hitTestRadius * 2.0);
		List<int> elements = nodeSpatialIndex.GetElements(extent);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			Point point = Nodes[element].Point;
			if (extent.Contains(point))
			{
				elementIndex = element;
				return true;
			}
		}
		return false;
	}

	public bool HitTestArc(Point queryPt, HitTestSortOrder mode, double segmentHitTestRadius, double internalPointHitTestRadius, out NetworkElementType element, out int elementIndex, out int subElementIndex)
	{
		element = NetworkElementType.ArcSegment;
		elementIndex = -1;
		subElementIndex = -1;
		if (Arcs.Count == 0)
		{
			return false;
		}
		double segmentHitTestRadiusPolynomial = segmentHitTestRadius * segmentHitTestRadius;
		double internalPointHitTestRadiusPolynomial = internalPointHitTestRadius * internalPointHitTestRadius;
		Extent extent = Extent.CenteredOn(queryPt, segmentHitTestRadius * 2.0);
		Extent other = Extent.CenteredOn(queryPt, internalPointHitTestRadius * 2.0);
		Extent extent2 = extent.Union(other);
		List<FeatureSegmentSubset> elements = ArcSpatialIndex.GetElements(extent2);
		int elementIndex2 = -1;
		int subElementIndex2 = -1;
		double maxValue = double.MaxValue;
		for (int i = 0; i < elements.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = elements[i];
			int fid = featureSegmentSubset.Fid;
			if (fid < 0 || fid >= Arcs.Count)
			{
				_logger.LogWarning("Bad Arc FID value in HitTestArc");
				continue;
			}
			int internalPointCount = Arcs[fid].InternalPointCount;
			if (internalPointCount == 0)
			{
				continue;
			}
			PointCollection internalPoints = Arcs[fid].InternalPoints;
			for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
			{
				Range1D range1D = featureSegmentSubset.Ranges[j];
				int startOffset = Math.Max(0, range1D.Start - 1);
				int count = Math.Min(internalPointCount - 1, range1D.End);
				for (int k = startOffset; k <= count; k++)
				{
					Point pt = internalPoints[k];
					if (!other.Contains(pt))
					{
						continue;
					}
					double ptQueryPtTerm = pt.X - queryPt.X;
					double ptQueryPtTerm2 = pt.Y - queryPt.Y;
					double maxValue3 = ptQueryPtTerm * ptQueryPtTerm + ptQueryPtTerm2 * ptQueryPtTerm2;
					if (maxValue3 <= internalPointHitTestRadiusPolynomial && maxValue3 < maxValue)
					{
						if (mode == HitTestSortOrder.Index)
						{
							element = NetworkElementType.ArcInternalPoint;
							elementIndex = fid;
							subElementIndex = k;
							return true;
						}
						elementIndex2 = fid;
						subElementIndex2 = k;
						maxValue = maxValue3;
					}
				}
			}
		}
		if (elementIndex2 != -1)
		{
			element = NetworkElementType.ArcInternalPoint;
			elementIndex = elementIndex2;
			subElementIndex = subElementIndex2;
			return true;
		}
		int elementIndex3 = -1;
		int subElementIndex3 = -1;
		double maxValue2 = double.MaxValue;
		for (int l = 0; l < elements.Count; l++)
		{
			FeatureSegmentSubset featureSegmentSubset2 = elements[l];
			int fid2 = featureSegmentSubset2.Fid;
			if (fid2 < 0 || fid2 >= Arcs.Count)
			{
				_logger.LogWarning("Bad FSS Arc FID in HitTestArc");
				continue;
			}
			int arcSegmentCount = GetArcSegmentCount(Arcs[fid2]);
			for (int m = 0; m < featureSegmentSubset2.Ranges.Count; m++)
			{
				Range1D range1D2 = featureSegmentSubset2.Ranges[m];
				for (int n = range1D2.Start; n <= range1D2.End; n++)
				{
					if (n < 0 || n >= arcSegmentCount)
					{
						_logger.LogWarning("Bad FSS Segment FID in HitTestArc");
						continue;
					}
					double distanceToPointSquared = GetSegmentOnArc(fid2, n).DistanceToPointSquared(queryPt);
					if (distanceToPointSquared <= segmentHitTestRadiusPolynomial && distanceToPointSquared < maxValue2)
					{
						if (mode == HitTestSortOrder.Index)
						{
							elementIndex = fid2;
							subElementIndex = n;
							return true;
						}
						elementIndex3 = fid2;
						subElementIndex3 = n;
						maxValue2 = distanceToPointSquared;
					}
				}
			}
		}
		if (elementIndex3 == -1)
		{
			return false;
		}
		elementIndex = elementIndex3;
		subElementIndex = subElementIndex3;
		return true;
	}

	public bool HitTestArcs(Point queryPt, double segmentHitTestRadius, double internalPointHitTestRadius, out NetworkElementType element, out IList<FeatureSegmentSubset> hitElements, int maximumReturns = 5)
	{
		element = NetworkElementType.ArcSegment;
		hitElements = default(CompactList<FeatureSegmentSubset>);
		double segmentHitTestRadiusPolynomial = segmentHitTestRadius * segmentHitTestRadius;
		if (Arcs.Count == 0)
		{
			return false;
		}
		SpatialIndex<FeatureSegmentSubset> arcSpatialIndex = ArcSpatialIndex;
		Extent extent = Extent.CenteredOn(queryPt, segmentHitTestRadius * 2.0);
		Extent other = Extent.CenteredOn(queryPt, internalPointHitTestRadius * 2.0);
		Extent extent2 = extent.Union(other);
		List<FeatureSegmentSubset> elements = arcSpatialIndex.GetElements(extent2);
		for (int i = 0; i < elements.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = elements[i];
			int fid = featureSegmentSubset.Fid;
			if (fid < 0 || fid >= Arcs.Count)
			{
				continue;
			}
			int getArcSegmentCountArcsTerm = GetArcSegmentCount(Arcs[fid]) - 1;
			FeatureSegmentSubset featureSegmentSubset2 = new FeatureSegmentSubset();
			featureSegmentSubset2.Fid = fid;
			FeatureSegmentSubset featureSegmentSubset22 = featureSegmentSubset2;
			Range1D range1D2 = new Range1D(int.MinValue, int.MinValue);
			for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
			{
				Range1D range1D = featureSegmentSubset.Ranges[j];
				for (int k = Math.Max(0, range1D.Start); k <= Math.Min(range1D.End, getArcSegmentCountArcsTerm); k++)
				{
					if (GetSegmentOnArc(fid, k).DistanceToPointSquared(queryPt) > segmentHitTestRadiusPolynomial)
					{
						continue;
					}
					if (range1D2.End != k - 1)
					{
						if (range1D2.End != int.MinValue)
						{
							featureSegmentSubset22.Ranges.Add(range1D2);
						}
						range1D2 = new Range1D(k, k);
					}
					else
					{
						range1D2 = new Range1D(range1D2.Start, k);
					}
				}
			}
			featureSegmentSubset22.Ranges.Add(range1D2);
			if (range1D2.End != int.MinValue)
			{
				hitElements.Add(featureSegmentSubset22);
			}
			if (hitElements.Count >= maximumReturns)
			{
				return true;
			}
		}
		return hitElements.Count > 0;
	}

	private Point NodePoint(NodeArcConnection conn)
	{
		return Nodes[NodeIndex(conn)].Point;
	}

	private int NodeIndex(NodeArcConnection conn)
	{
		BaseArc baseArc = Arcs[conn.ArcID];
		if (conn.IsStart)
		{
			return baseArc.StartNode;
		}
		return baseArc.EndNode;
	}

	public IReadOnlyList<Polyline> GetAllArcPolylines()
	{
		long arcCacheVersion = GetArcCacheVersion();
		Polyline[] arcPolylineCache = _arcPolylineCache;
		if (arcPolylineCache == null || arcPolylineCache.Length != Arcs.Count || _lastCachedPolylineVersion != arcCacheVersion)
		{
			arcPolylineCache = (_arcPolylineCache = new Polyline[Arcs.Count]);
		}
		_lastCachedPolylineVersion = arcCacheVersion;
		int arcPolylineCacheLength = arcPolylineCache.Length;
		for (int i = 0; i < arcPolylineCacheLength; i++)
		{
			Polyline[] arcPolylineCacheArray = arcPolylineCache;
			int ordinal = i;
			if (arcPolylineCacheArray[ordinal] == null)
			{
				arcPolylineCacheArray[ordinal] = BuildArcPolyline_ZachMode(i);
			}
		}
		return arcPolylineCache;
	}

	public double GetArcPolylineLength(int arcIdx)
	{
		return GetArcPolyline(arcIdx).Length;
	}

	public Polyline GetArcPolyline(int arcIdx)
	{
		long arcCacheVersion = GetArcCacheVersion();
		Polyline[] arcPolylineCache = _arcPolylineCache;
		if (arcPolylineCache == null || arcPolylineCache.Length != Arcs.Count || _lastCachedPolylineVersion != arcCacheVersion)
		{
			arcPolylineCache = (_arcPolylineCache = new Polyline[Arcs.Count]);
		}
		Polyline polyline = arcPolylineCache[arcIdx];
		if (polyline != null)
		{
			return polyline;
		}
		_lastCachedPolylineVersion = arcCacheVersion;
		return arcPolylineCache[arcIdx] = BuildArcPolyline_ZachMode(arcIdx);
	}

	protected Polyline BuildArcPolyline(int arcIdx)
	{
		BaseArc baseArc = Arcs[arcIdx];
		if (baseArc.StartNode < 0 || baseArc.StartNode >= _nodes.Count)
		{
			return null;
		}
		if (baseArc.EndNode < 0 || baseArc.EndNode >= _nodes.Count)
		{
			return null;
		}
		Point point = _nodes[baseArc.StartNode].Point;
		Point point2 = _nodes[baseArc.EndNode].Point;
		if (baseArc.InternalPoints.IsNullOrEmpty())
		{
			return Polyline.CreateWithBuffer(new Point[2] { point, point2 });
		}
		List<Point> points = new List<Point>(baseArc.InternalPointCount + 2);
		points.Add(point);
		points.AddRange(baseArc.InternalPoints);
		points.Add(point2);
		return Polyline.CreateWithBuffer(points);
	}

	protected Polyline BuildArcPolyline_ZachMode(int arcIdx)
	{
		BaseArc baseArc = Arcs[arcIdx];
		bool startNodeBaseArcTerm = baseArc.StartNode >= 0 && baseArc.StartNode < _nodes.Count;
		bool endNodeBaseArcTerm = baseArc.EndNode >= 0 && baseArc.EndNode < _nodes.Count;
		if (!startNodeBaseArcTerm && !endNodeBaseArcTerm)
		{
			throw new Exception($"Arc {arcIdx} has invalid node indices ({baseArc.StartNode}, {baseArc.EndNode}), when there are {_nodes.Count} nodes.");
		}
		if (baseArc.InternalPoints.IsNullOrEmpty())
		{
			if (!startNodeBaseArcTerm)
			{
				return new Polyline(new Point[1] { _nodes[baseArc.EndNode].Point });
			}
			if (!endNodeBaseArcTerm || baseArc.StartNode == baseArc.EndNode)
			{
				return new Polyline(new Point[1] { _nodes[baseArc.StartNode].Point });
			}
			return new Polyline(new Point[2]
			{
				_nodes[baseArc.StartNode].Point,
				_nodes[baseArc.EndNode].Point
			});
		}
		List<Point> points = new List<Point>(baseArc.InternalPointCount + 2);
		if (startNodeBaseArcTerm)
		{
			points.Add(_nodes[baseArc.StartNode].Point);
		}
		points.AddRange(baseArc.InternalPoints);
		if (endNodeBaseArcTerm)
		{
			points.Add(_nodes[baseArc.EndNode].Point);
		}
		return Polyline.CreateWithBuffer(points);
	}

	public bool IsAnyArcConnectedToNode(int nodeId)
	{
		for (int i = 0; i < Arcs.Count; i++)
		{
			BaseArc baseArc = Arcs[i];
			if (baseArc.StartNode == nodeId)
			{
				return true;
			}
			if (baseArc.EndNode == nodeId)
			{
				return true;
			}
		}
		return false;
	}

	public Segment GetSimplifiedArcSegment(int arcIdx)
	{
		BaseArc baseArc = Arcs[arcIdx];
		Point point = _nodes[baseArc.StartNode].Point;
		Point point2 = _nodes[baseArc.EndNode].Point;
		return new Segment(point, point2);
	}

	public Point GetArcMidpoint(int arcIdx)
	{
		BaseArc baseArc = Arcs[arcIdx];
		if (baseArc.StartNode == -1 || baseArc.EndNode == -1)
		{
			return Point.NullPoint;
		}
		Point point = _nodes[baseArc.StartNode].Point;
		Point point2 = _nodes[baseArc.EndNode].Point;
		if (baseArc.InternalPointCount == 0)
		{
			return point.Midpoint(point2);
		}
		return GetArcPolyline(arcIdx).MidPointOnPolyline().Point;
	}

	public Segment GetSegmentOnArc(int arcIdx, int segIdx)
	{
		BaseArc baseArc = Arcs[arcIdx];
		if (baseArc.InternalPointCount == 0)
		{
			return GetSegmentOnArcNoInternalPoints(baseArc, segIdx);
		}
		return GetSegmentOnArcWithInternalPoints(baseArc, segIdx);
	}

	public Segment GetSegmentOnArcNoInternalPoints(BaseArc a, int segIdx = 0)
	{
		if (segIdx > 0)
		{
			throw new ArgumentOutOfRangeException("segIdx", segIdx, "Arc without internal points only has segment 0.");
		}
		if (a.StartNode < 0 || a.StartNode >= _nodes.Count || a.EndNode < 0 || a.EndNode >= _nodes.Count)
		{
			throw new ArgumentOutOfRangeException("a", $"Arc has invalid node indices ({a.StartNode}, {a.EndNode}), when there are {_nodes.Count} nodes.");
		}
		Point point = _nodes[a.StartNode].Point;
		Point point2 = _nodes[a.EndNode].Point;
		return new Segment(point, point2);
	}

	public int GetArcSegmentCount(int arcIndex)
	{
		return Arcs[arcIndex].InternalPointCount + 1;
	}

	public int GetArcSegmentCount(BaseArc a)
	{
		return a.InternalPointCount + 1;
	}

	public Segment GetSegmentOnArcWithInternalPoints(BaseArc a, int segIdx)
	{
		if (segIdx == 0)
		{
			return new Segment(_nodes[a.StartNode].Point, a.InternalPoints[segIdx]);
		}
		if (segIdx < a.InternalPointCount)
		{
			return new Segment(a.InternalPoints[segIdx - 1], a.InternalPoints[segIdx]);
		}
		if (segIdx == a.InternalPointCount)
		{
			PointCollection internalPoints = a.InternalPoints;
			return new Segment(internalPoints[internalPoints.Count - 1], _nodes[a.EndNode].Point);
		}
		throw new ArgumentOutOfRangeException("segIdx", segIdx, $"Arc only has {a.InternalPointCount + 1} segments.");
	}

	public virtual void TryFixInvalidStates()
	{
		int[] intValues = new int[_nodes.Count];
		HashSet<int> intSet = new HashSet<int>();
		int ordinal = 0;
		for (int i = 0; i < Arcs.Count; i++)
		{
			BaseArc baseArc = Arcs[i];
			if (baseArc.StartNode == -1 || baseArc.EndNode == -1)
			{
				intSet.Add(i);
				continue;
			}
			Polyline polyline = BuildArcPolyline_ZachMode(i);
			if (polyline == null || polyline.Points.Count <= 1 || polyline.Length < double.Epsilon)
			{
				intSet.Add(i);
				continue;
			}
			if (baseArc.StartNode == baseArc.EndNode && baseArc.InternalPointCount == 1)
			{
				intSet.Add(i);
				continue;
			}
			if (intValues[baseArc.StartNode] == 0)
			{
				ordinal++;
			}
			intValues[baseArc.StartNode] = 1;
			if (intValues[baseArc.EndNode] == 0)
			{
				ordinal++;
			}
			intValues[baseArc.EndNode] = 1;
			Point other = Nodes[baseArc.StartNode].Point;
			Point other2 = Nodes[baseArc.EndNode].Point;
			while (baseArc.InternalPointCount != 0)
			{
				if (baseArc.InternalPoints[0].EqualXY(in other))
				{
					baseArc.InternalPoints.RemoveAt(0);
					continue;
				}
				PointCollection internalPoints = baseArc.InternalPoints;
				if (!internalPoints[internalPoints.Count - 1].EqualXY(in other2))
				{
					break;
				}
				baseArc.InternalPoints.RemoveAt(baseArc.InternalPointCount - 1);
			}
			for (int j = 0; j < baseArc.InternalPointCount - 1; j++)
			{
				if (baseArc.InternalPoints[j].EqualXY(baseArc.InternalPoints[j + 1]))
				{
					baseArc.InternalPoints.RemoveAt(j);
					j--;
				}
			}
		}
		bool nodesTerm = ordinal != Nodes.Count;
		bool condition = intSet.Count != 0;
		if (nodesTerm)
		{
			RemoveNodesByMask(intValues);
		}
		if (!condition)
		{
			return;
		}
		foreach (int orderDescending in intSet.OrderDescending())
		{
			RemoveArc(orderDescending, liveUpdateTopology: false);
		}
	}

	protected virtual Extent ComputeExtent()
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < Nodes.Count; i++)
		{
			Point point = Nodes[i].Point;
			maxValue = Math.Min(maxValue, point.X);
			minValue = Math.Max(minValue, point.X);
			maxValue2 = Math.Min(maxValue2, point.Y);
			minValue2 = Math.Max(minValue2, point.Y);
		}
		for (int j = 0; j < Arcs.Count; j++)
		{
			PointCollection internalPoints = Arcs[j].InternalPoints;
			if (!internalPoints.IsNullOrEmpty())
			{
				for (int k = 0; k < internalPoints.Count; k++)
				{
					Point point2 = internalPoints[k];
					maxValue = Math.Min(maxValue, point2.X);
					minValue = Math.Max(minValue, point2.X);
					maxValue2 = Math.Min(maxValue2, point2.Y);
					minValue2 = Math.Max(minValue2, point2.Y);
				}
			}
		}
		if (maxValue > minValue || maxValue2 > minValue2)
		{
			return Extent.NullExtent;
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	private SpatialIndex<int> ComputeNodeSpatialIndex()
	{
		if (Nodes.Count == 0 || Extent.IsNull())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(SpatialIndexExtent, Nodes.Count);
		for (int i = 0; i < Nodes.Count; i++)
		{
			spatialIndex.Add(Nodes[i].Point, i);
		}
		return spatialIndex;
	}

	private SpatialIndex<Point> ComputeNodePointsSpatialIndex()
	{
		if (Nodes.Count == 0 || Extent.IsNull())
		{
			return SpatialIndex<Point>.DefaultSpatialIndex();
		}
		SpatialIndex<Point> spatialIndex = new SpatialIndex<Point>(SpatialIndexExtent, Nodes.Count);
		for (int i = 0; i < Nodes.Count; i++)
		{
			Point point = Nodes[i].Point;
			spatialIndex.Add(point, point);
		}
		return spatialIndex;
	}

	private bool NodeInvalid(int nodeIdx)
	{
		if (nodeIdx >= 0)
		{
			return nodeIdx >= Nodes.Count;
		}
		return true;
	}

	private SpatialIndex<FeatureSegmentSubset> ComputeArcSpatialIndex()
	{
		if (Arcs.Count == 0 || Extent.IsNull())
		{
			return SpatialIndex<FeatureSegmentSubset>.DefaultSpatialIndex();
		}
		int ordinal = 0;
		for (int i = 0; i < Arcs.Count; i++)
		{
			ordinal += 1 + (Arcs[i].InternalPoints?.Count ?? 0);
		}
		SpatialIndex<FeatureSegmentSubset> spatialIndex = new SpatialIndex<FeatureSegmentSubset>(SpatialIndexExtent, SpatialIndex.ReasonableBinCount(ordinal));
		for (int j = 0; j < Arcs.Count; j++)
		{
			BaseArc baseArc = Arcs[j];
			if (NodeInvalid(baseArc.StartNode) || NodeInvalid(baseArc.EndNode))
			{
				continue;
			}
			if (baseArc.InternalPoints.IsNullOrEmpty())
			{
				Segment segment = new Segment(Nodes[baseArc.StartNode].Point, Nodes[baseArc.EndNode].Point);
				segment.Rasterize(t: (spatialIndex, j, 0), raster: spatialIndex.Raster, pixelAction: SpatialIndex.RasterizeFSSOperator);
				continue;
			}
			Segment segment2 = new Segment(Nodes[baseArc.StartNode].Point, baseArc.InternalPoints[0]);
			segment2.Rasterize(t: (spatialIndex, j, 0), raster: spatialIndex.Raster, pixelAction: SpatialIndex.RasterizeFSSOperator);
			for (int k = 1; k < baseArc.InternalPointCount; k++)
			{
				Point ptA = baseArc.InternalPoints[k - 1];
				Point ptB = baseArc.InternalPoints[k];
				segment2 = new Segment(ptA, ptB);
				segment2.Rasterize(t: (spatialIndex, j, k), raster: spatialIndex.Raster, pixelAction: SpatialIndex.RasterizeFSSOperator);
			}
			PointCollection internalPoints = baseArc.InternalPoints;
			segment2 = new Segment(internalPoints[internalPoints.Count - 1], Nodes[baseArc.EndNode].Point);
			segment2.Rasterize(t: (spatialIndex, j, baseArc.InternalPointCount), raster: spatialIndex.Raster, pixelAction: SpatialIndex.RasterizeFSSOperator);
		}
		return spatialIndex;
	}

	public virtual int GetDataHash()
	{
		int v = 0;
		for (int i = 0; i < (GlobalMetadata?.Rows.Count ?? 0); i++)
		{
			v = StableHash.Combine(v, HashRow(GlobalMetadata.Rows[i].ItemArray));
		}
		int combine = 0;
		for (int j = 0; j < Arcs.Count; j++)
		{
			BaseArc baseArc = Arcs[j];
			if (!baseArc.InternalPoints.IsNullOrEmpty())
			{
				combine = StableHash.Combine(combine, baseArc.InternalPoints.GetDataHash());
			}
			combine = StableHash.Combine(combine, baseArc.StartNode);
			combine = StableHash.Combine(combine, baseArc.EndNode);
			combine = StableHash.Combine(combine, HashRow(ArcMetadata.Rows[j].ItemArray));
		}
		int combine2 = 0;
		for (int k = 0; k < Nodes.Count; k++)
		{
			Node node = Nodes[k];
			combine2 = StableHash.Combine(combine2, node.Point);
			combine2 = StableHash.Combine(combine2, HashRow(NodeMetadata.Rows[k].ItemArray));
		}
		return StableHash.Combine(v, combine, combine2);
	}

	public int GetArcSpatialHash(int arcIdx)
	{
		BaseArc baseArc = Arcs[arcIdx];
		Node node = Nodes[baseArc.StartNode];
		Node node2 = Nodes[baseArc.EndNode];
		if (baseArc.InternalPointCount > 0)
		{
			int v = node.Point.GetHashCode();
			for (int i = 0; i < baseArc.InternalPointCount; i++)
			{
				v = StableHash.Combine(v, baseArc.InternalPoints[i].GetHashCode());
			}
			return StableHash.Combine(v, node2.Point.GetHashCode());
		}
		return StableHash.Combine(node.Point.GetHashCode(), node2.Point.GetHashCode());
	}

	protected int GetArcFullHash(int arcId)
	{
		int arcSpatialHash = GetArcSpatialHash(arcId);
		int arcMetadataHash = GetArcMetadataHash(arcId);
		return HashCode.Combine(arcSpatialHash, arcMetadataHash);
	}

	protected int GetArcMetadataHash(int arcId)
	{
		return HashRow(ArcMetadata.Rows[arcId].ItemArray);
	}

	protected static int HashRow(object[] data)
	{
		int combine = 0;
		for (int i = 0; i < data.Length; i++)
		{
			combine = StableHash.Combine(combine, data[i]?.GetHashCode() ?? 0);
		}
		return combine;
	}

	protected virtual void ResetCacheStateForClone()
	{
		_arcTracker = new CollectionVersionTracker();
		_extVersion.Item = default;
		_extVersion.Version = long.MinValue;
		_nodeSpIdxCache.Item = null;
		_nodeSpIdxCache.Version = long.MinValue;
		_nodePointsSpIdxCache.Item = null;
		_nodePointsSpIdxCache.Version = long.MinValue;
		_arcSpIdxCache.Item = null;
		_arcSpIdxCache.Version = long.MinValue;
		_arcPolylineCache = null;
	}

	public virtual bool HitTest(Point queryPt, double nodeWorldSpaceHitDistance, double arcSegmentWorldSpaceHitDistance, double arcInternalPointWorldSpaceHitDistance, out NetworkElementType element, out int elementIndex, out int subElementIndex)
	{
		if (HitTestNode(queryPt, nodeWorldSpaceHitDistance, out elementIndex))
		{
			element = NetworkElementType.Node;
			subElementIndex = -1;
			return true;
		}
		return HitTestArc(queryPt, HitTestSortOrder.Distance, arcSegmentWorldSpaceHitDistance, arcInternalPointWorldSpaceHitDistance, out element, out elementIndex, out subElementIndex);
	}

	public virtual void CopyArcAttributes(int fromArcFid, int toArcFid)
	{
		ArcMetadata.SetRow(toArcFid, ArcMetadata[fromArcFid].ItemArray.ToArray());
	}

	private void OnReadLockAcquisitionFailure()
	{
		_logger.LogWarning("Topology failed to acquire reader lock. This can happen when modifying a topology layer during a complete operation.");
	}

	private void OnWriteLockAcquisitionFailure()
	{
		_logger.LogWarning("Topology failed to acquire writer lock. This can happen when modifying a topology layer during a complete operation.");
	}

	protected void LogIfFrozen(string operationName)
	{
		if (_freezer.IsFrozen)
		{
			_logger.LogWarning("{Operation} is executing on a frozen topology ({FrozenCount} active freezes). Destructive operations are very fragile if run like this.", operationName, _freezer.FrozenCount);
		}
	}
}
public abstract class BaseTopology<TArc> : BaseTopology, IStreamableObject where TArc : BaseArc, IEquatable<TArc>, new()
{
	protected readonly List<TArc> _arcs;

	public new IReadOnlyList<TArc> Arcs => _arcs;

	public BaseTopology()
		: this(new List<TArc>())
	{
	}

	private BaseTopology(List<TArc> arcs)
		: base(arcs)
	{
		_arcs = arcs;
	}

	[Conditional("DEBUG")]
	protected void DebugAssertArcListShared()
	{
	}

	protected override void RemoveArcAt(int arcFid)
	{
		_arcs.RemoveAt(arcFid);
	}

	protected abstract TArc BuildArc(int startNode, int endNode, List<Point> internalVertices);

	public override TableRow AddArc(int startNode, int endNode, List<Point> internalVertexes, out int fid)
	{
		TArc arc = BuildArc(startNode, endNode, internalVertexes);
		return AddArc(arc, out fid);
	}

	public TableRow AddArc(TArc arc)
	{
		int fid;
		return AddArc(arc, out fid);
	}

	public TableRow AddArc(TArc arc, out int fid)
	{
		_arcs.Add(arc);
		TableRow result = ArcMetadata.AddNewRow(out fid);
		AddUnsortedNodeArcConnection(fid);
		return result;
	}

	public void AddPolylineAsArc(Polyline pline, double endpointTol)
	{
		if (pline == null || pline.PointCount < 2)
		{
			throw new ArgumentException("pline");
		}
		Point point = pline.First();
		Point point2 = pline.Last();
		TArc tArc = new TArc();
		if (HitTestNode(point, endpointTol, out var elementIndex))
		{
			tArc.StartNode = elementIndex;
		}
		else
		{
			AddNode(point);
			tArc.StartNode = Nodes.Count - 1;
		}
		if (HitTestNode(point2, endpointTol, out elementIndex))
		{
			tArc.EndNode = elementIndex;
		}
		else
		{
			AddNode(point2);
			tArc.EndNode = Nodes.Count - 1;
		}
		Memory<Point> pointBufferUnsafe = pline.GetPointBufferUnsafe();
		tArc.AddPoints(pointBufferUnsafe.Span.Slice(1, pointBufferUnsafe.Length - 2));
		AddArc(tArc, out var _);
	}

	public void AddSegmentAsArc(Segment seg, double endpointTol)
	{
		TArc tArc = new TArc();
		Point a = seg.A;
		Point b = seg.B;
		if (HitTestNode(a, endpointTol, out var elementIndex))
		{
			tArc.StartNode = elementIndex;
		}
		else
		{
			AddNode(a);
			tArc.StartNode = Nodes.Count - 1;
		}
		if (HitTestNode(b, endpointTol, out elementIndex))
		{
			tArc.EndNode = elementIndex;
		}
		else
		{
			AddNode(b);
			tArc.EndNode = Nodes.Count - 1;
		}
		AddArc(tArc, out var _);
	}

	public TableRow InsertArc(int idx, TArc arc)
	{
		_arcs.Insert(idx, arc);
		foreach (Node node in Nodes)
		{
			for (int i = 0; i < node.Arcs.Count; i++)
			{
				NodeArcConnection nodeArcConnection = node.Arcs[i];
				if (nodeArcConnection.ArcID >= idx)
				{
					node.Arcs[i] = new NodeArcConnection(nodeArcConnection.ArcID + 1, nodeArcConnection.IsStart);
				}
			}
		}
		AddUnsortedNodeArcConnection(idx);
		return ArcMetadata.InsertRow(idx);
	}

	public bool PartsAreEqual(BaseTopology<TArc> other)
	{
		if (Arcs.Count != other.Arcs.Count)
		{
			return false;
		}
		if (Nodes.Count != other.Nodes.Count)
		{
			return false;
		}
		for (int i = 0; i < Arcs.Count; i++)
		{
			if (!Arcs[i].Equals(other.Arcs[i]))
			{
				return false;
			}
		}
		for (int j = 0; j < Nodes.Count; j++)
		{
			if (!Nodes[j].Equals(other.Nodes[j]))
			{
				return false;
			}
		}
		return true;
	}

	protected long DebugGetVersion()
	{
		return long.MinValue;
	}

	protected void DebugAssertVersionIncreased(long initialVersion)
	{
	}

	public virtual void StreamIn(BinaryReader br)
	{
		LogIfFrozen("StreamIn");
		long initialVersion = DebugGetVersion();
		long arcCacheVersion = GetArcCacheVersion();
		_arcs.Clear();
		TArc[] streamCollectionIn = br.StreamCollectionIn<TArc>();
		if (streamCollectionIn != null && streamCollectionIn.Length != 0)
		{
			_arcs.AddRange(streamCollectionIn);
		}
		_nodes.Clear();
		Node[] streamCollectionInArray = br.StreamCollectionIn<Node>();
		if (streamCollectionInArray != null && streamCollectionInArray.Length != 0)
		{
			_nodes.AddRange(streamCollectionInArray);
		}
		_arcTracker.OnDeleted(arcCacheVersion);
		_nodeVersion++;
		DebugAssertVersionIncreased(initialVersion);
	}

	public virtual void StreamOut(BinaryWriter bw)
	{
		bw.StreamCollectionOut(_arcs);
		bw.StreamCollectionOut(_nodes);
	}
}
