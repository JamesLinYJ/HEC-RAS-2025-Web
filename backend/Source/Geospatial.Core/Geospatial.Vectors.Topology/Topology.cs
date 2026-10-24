using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Geospatial.DataStructures;
using Geospatial.Helpers;
using Geospatial.Vectors.Intersections;
using Geospatial.Vectors.MeshTopology;
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

public class Topology : BaseTopology<Arc>, IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, IStreamableObject
{
	public struct TopologyTinCache
	{
		public Tin Tin;

		public Extent TopologyExtent;

		public bool IsNull => Tin == null;
	}

	private struct RegionReverseLookupData
	{
		public Region RegionA = null;

		public Region RegionB = null;

		public RegionReverseLookupData()
		{
		}
	}

	public struct HealCommand
	{
		public int ControlPointIndex { get; init; } = 0;

		public int ToRegionIndex { get; init; } = -1;

		public int FromRegionIndex { get; init; } = -1;

		public Point FromPoint { get; init; } = default;

		public Point ToPoint { get; init; } = default;

		public HealCommand()
		{
		}
	}

	private class RegionRingPackage
	{
		public RegionRing Ring;

		public int TriangleIdx;

		public HashSet<int> HangingArcs;
	}

	public enum RegionCompletionSeverity
	{
		Fatal,
		Ineffectual
	}

	public enum Reorder
	{
		Off,
		Global,
		Regions,
		Random
	}

	public class TopologyMessageClass : MessageClass
	{
		public RegionCompletionSeverity RegionCompletionSeverity { get; }

		public TopologyMessageClass(string code, MessageSeverity severity, RegionCompletionSeverity regionSeverity, string message)
			: base(code, severity, message)
		{
			RegionCompletionSeverity = regionSeverity;
		}
	}

	public static class MessageCodes
	{
		public static readonly TopologyMessageClass FloatingArc = new TopologyMessageClass("Topology_FloatingArc", MessageSeverity.Error, RegionCompletionSeverity.Ineffectual, "Arc {ArcIndex} is floating.");

		public static readonly TopologyMessageClass ExternalArc = new TopologyMessageClass("Topology_ExternalArc", MessageSeverity.Error, RegionCompletionSeverity.Ineffectual, "Arc {ArcIndex} is outside of all regions.");

		public static readonly TopologyMessageClass ArcIndexingError = new TopologyMessageClass("Topology_ArcIndexingError", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Arc {ArcIndex} had an indexing error.");

		public static readonly TopologyMessageClass ArcNotInTriangulation = new TopologyMessageClass("Topology_ArcNotInTriangulation", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Triangulation error: Arc {ArcIndex} near ({Coordinate}) was not preserved as a constrained edge. It may be overlapping or coincident with another arc.");

		public static readonly TopologyMessageClass FloatingNode = new TopologyMessageClass("Topology_FloatingNode", MessageSeverity.Error, RegionCompletionSeverity.Ineffectual, "Node {NodeIndex} is floating.");

		public static readonly TopologyMessageClass NodeIndexingError = new TopologyMessageClass("Topology_NodeIndexingError", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Node {NodeIndex} has an indexing error.");

		public static readonly TopologyMessageClass RegionIndexingError = new TopologyMessageClass("Topology_RegionIndexingError", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Region {RegionIndex} has an indexing error.");

		public static readonly TopologyMessageClass RegionWithMultipleControlPoints = new TopologyMessageClass("Topology_MultipleControlPoints", MessageSeverity.Error, RegionCompletionSeverity.Ineffectual, "Region {RegionIndex} has multiple control points.");

		public static readonly TopologyMessageClass RegionWithZeroArea = new TopologyMessageClass("Topology_RegionWithZeroArea", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Region {RegionIndex} has no area. There must be overlapping arcs or an issue nearby.");

		public static readonly TopologyMessageClass SelfLoopingArc = new TopologyMessageClass("Topology_SelfLoopingArc", MessageSeverity.Warning, RegionCompletionSeverity.Ineffectual, "Arc {ArcIndex} starts and ends on the same node.");

		public static readonly TopologyMessageClass ArcIdenticalEndpointAndNodeIdentical = new TopologyMessageClass("Topology_ArcIdenticalEndpointAndNode", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Arc {ArcIndex} has internal points identical to its start/end node.");

		public static readonly TopologyMessageClass HangingArc = new TopologyMessageClass("Topology_HangingArc", MessageSeverity.Error, RegionCompletionSeverity.Ineffectual, "Arc {ArcIndex} is hanging.");

		public static readonly TopologyMessageClass IncompleteArc = new TopologyMessageClass("Topology_IncompleteArc", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Arc {ArcIndex} is incomplete.");

		public static readonly TopologyMessageClass NullNodeArc = new TopologyMessageClass("Topology_ArcNullNode", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Arc {ArcIndex} is not attached to an arc on both ends.");

		public static readonly TopologyMessageClass ArcNodeConnectionInvalid = new TopologyMessageClass("Topology_ArcNodeConnectionInvalid", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Node {NodeIndex} has an invalid connection to one or more of its arcs, check for nearby intersecting or coincident arcs.");

		public static readonly TopologyMessageClass ArcsIntersect = new TopologyMessageClass("Topology_ArcsIntersect", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Arc {ArcIndex} intersects arc {ArcIndex}.");

		public static readonly TopologyMessageClass CoincidentNodes = new TopologyMessageClass("Topology_CoincidentNodes", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Node {NodeIndex} is coincident with node {NodeIndex}.");

		public static readonly TopologyMessageClass ArcsOverlapping = new TopologyMessageClass("Topology_ArcsOverlapping", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Arc {ArcIndex} overlaps Arc {ArcIndex}.");

		public static readonly TopologyMessageClass NoArcs = new TopologyMessageClass("Topology_NoArcs", MessageSeverity.Error, RegionCompletionSeverity.Ineffectual, "There are no arcs.");

		public static readonly TopologyMessageClass UnknownTopologyFailure = new TopologyMessageClass("Topology_UnknownFailure", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Topology completion encountered an unknown build error.");

		public static readonly TopologyMessageClass UnknownElementFailure = new TopologyMessageClass("Topology_UnknownElementFailure", MessageSeverity.Error, RegionCompletionSeverity.Fatal, "Unknown error for element {ElementIndex}");
	}

	private const double ArcIntersectionEndPointTolerance = 0.999;

	private readonly List<Point> _regionControlPoints;

	private readonly List<Region> _regions;

	private long _ctrlPtVersion = 1L;

	private bool _isFirstComplete = true;

	private readonly Versioned<SpatialIndex<int>> _ctrlPtSpIdxCache = new Versioned<SpatialIndex<int>>();

	private readonly Versioned<SpatialIndex<int>> _regionSpIdxCache = new Versioned<SpatialIndex<int>>();

	private Versioned<Dictionary<int, int>> _controlPointContainingRegionCacheVersioned = new Versioned<Dictionary<int, int>>();

	private TopologyCompletionCache _completionCache;

	private long _lastCachedPolygonVersion = -1L;

	private Polygon[] _regionPolygonsCache;

	private ComplexPolygon[] _regionComplexPolygonsCache;

	private Point[] _regionBestControlPointCache;

	private readonly Versioned<TinSizeFunction> _sizeFunctionCache = new Versioned<TinSizeFunction>();

	public Reorder ReorderSwitch { get; set; } = Reorder.Regions;

	public SpatialIndex<int> ControlPointSpatialIndex => _ctrlPtSpIdxCache.Get(_ctrlPtVersion, ComputeControlPointSpatialIndex);

	public SpatialIndex<int> RegionSpatialIndex => _regionSpIdxCache.Get(GetRegionCacheVersion(), ComputeRegionSpatialIndex);

	private Dictionary<int, int> _controlPointContainingRegionCache => _controlPointContainingRegionCacheVersioned.Get(VersionXY, BuildControlPointContainingRegionCache);

	public TinSizeFunction GlobalSizeFunction => _sizeFunctionCache.Get(_globalVersion, ComputeGlobalSizeFunction);

	public IReadOnlyList<Point> RegionControlPoints => _regionControlPoints;

	public IReadOnlyList<Region> CompletedRegions => _regions;

	[DatasetName("Control Point Attributes")]
	public Table ControlPointMetadata { get; private set; }

	public long GetRegionCacheVersion()
	{
		return _globalVersion;
	}

	public Topology()
	{
		_regionControlPoints = new List<Point>();
		_regions = new List<Region>();
		ControlPointMetadata = new Table
		{
			InitValueTypesToDefault = true
		};
	}

	protected override long GetVersionInternal()
	{
		return base.GetVersionInternal() + _ctrlPtVersion;
	}

	public static Topology FromExtent(Extent ext, bool runComplete = true)
	{
		Topology topology = new Topology();
		topology.AddNode(ext.LowerLeft);
		topology.AddNode(ext.UpperLeft);
		topology.AddNode(ext.UpperRight);
		topology.AddNode(ext.LowerRight);
		topology.AddArc(0, 1);
		topology.AddArc(1, 2);
		topology.AddArc(2, 3);
		topology.AddArc(3, 0);
		topology.AddRegionControlPoint(ext.Center);
		if (runComplete)
		{
			OperationResult operationResult = topology.Complete();
			if (!operationResult)
			{
				throw new Exception(operationResult.GetConcatenatedMessages());
			}
		}
		return topology;
	}

	public void SetControlPointMetadataTable(Table tbl)
	{
		ControlPointMetadata = tbl;
	}

	public void UpdateControlPointPoint(int fid, Point newPoint)
	{
		if (!(_regionControlPoints[fid] == newPoint))
		{
			_regionControlPoints[fid] = newPoint;
			_ctrlPtVersion++;
		}
	}

	public TableRow AddRegionControlPoint(Point pt)
	{
		int fid;
		return AddRegionControlPoint(pt, out fid);
	}

	public TableRow AddRegionControlPoint(Point pt, out int fid)
	{
		_regionControlPoints.Add(pt);
		_ctrlPtVersion++;
		TableRow result = ControlPointMetadata.AddNewRow(out fid);
		List<int> elements = RegionSpatialIndex.GetElements(pt);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			if (RegionContainsPoint(element, pt))
			{
				CompletedRegions[element].TryAddControlPoint(fid);
				break;
			}
		}
		return result;
	}

	public TableRow InsertRegionControlPoint(int idx, Point pt)
	{
		_regionControlPoints.Insert(idx, pt);
		_ctrlPtVersion++;
		foreach (Region completedRegion in CompletedRegions)
		{
			for (int i = 0; i < completedRegion.ControlPointIndices.Count; i++)
			{
				if (completedRegion.ControlPointIndices[i] >= idx)
				{
					completedRegion.IncrementOrDecrementControlPointValue(i, increment: true);
				}
			}
		}
		return ControlPointMetadata.InsertRow(idx);
	}

	public void RemoveRegionControlPoint(int controlPointFID)
	{
		_regionControlPoints.RemoveAt(controlPointFID);
		ControlPointMetadata.RemoveRow(controlPointFID);
		foreach (Region completedRegion in CompletedRegions)
		{
			for (int i = 0; i < completedRegion.ControlPointIndices.Count; i++)
			{
				if (completedRegion.ControlPointIndices[i] == controlPointFID)
				{
					completedRegion.RemoveControlPointAt(i);
					i--;
				}
				else if (completedRegion.ControlPointIndices[i] > controlPointFID)
				{
					completedRegion.IncrementOrDecrementControlPointValue(i, increment: false);
				}
			}
		}
		_ctrlPtVersion++;
	}

	public OperationResult Complete(TopologyBuilderParams buildParams = null, TopologyBuildDiagnostics diagnostics = null, bool forceRecomplete = false)
	{
		if (_isFirstComplete)
		{
			TryFixInvalidStates();
			_isFirstComplete = false;
		}
		_globalVersion++;
		using (this.FreezeSafely())
		{
			if (buildParams == null)
			{
				buildParams = TopologyBuilderParams.Default();
			}
			if (!forceRecomplete && buildParams.AllowPartialRecomplete)
			{
				if (TryFastPathSingleArcEdit(buildParams, diagnostics, out var result))
				{
					if (buildParams.DifferentialValidatePartialRecomplete)
					{
						DifferentialValidate(result, buildParams, diagnostics, "fast-path");
					}
					return result;
				}
				if (TryPartialRecomplete(buildParams, diagnostics, out var result2))
				{
					if (buildParams.DifferentialValidatePartialRecomplete)
					{
						DifferentialValidate(result2, buildParams, diagnostics, "partial");
					}
					return result2;
				}
			}
			return CompleteInternal(buildParams, diagnostics);
		}
	}

	private void DifferentialValidate(OperationResult partialResult, TopologyBuilderParams buildParams, TopologyBuildDiagnostics diagnostics, string fastPathName)
	{
		try
		{
			Topology topology = Clone();
			TopologyBuilderParams buildParams2 = new TopologyBuilderParams
			{
				AllowFloatingArcs = buildParams.AllowFloatingArcs,
				AllowFloatingNodes = buildParams.AllowFloatingNodes,
				AllowMultipleControlPointsPerRegion = buildParams.AllowMultipleControlPointsPerRegion,
				AllowZeroControlPointsInRegion = buildParams.AllowZeroControlPointsInRegion,
				AllowArcToStartAndEndOnSameNode = buildParams.AllowArcToStartAndEndOnSameNode,
				MustBeEnclosed = buildParams.MustBeEnclosed,
				AllowPartialRecomplete = false,
				DifferentialValidatePartialRecomplete = false
			};
			OperationResult operationResult = topology.Complete(buildParams2, null, forceRecomplete: true);
			if (operationResult.Result != partialResult.Result)
			{
				BaseTopology._logger.LogWarning("Partial-recomplete differential mismatch ({path}): success {p} vs reference {r}", fastPathName, partialResult.Result, operationResult.Result);
				return;
			}
			if (topology.CompletedRegions.Count != _regions.Count)
			{
				BaseTopology._logger.LogWarning("Partial-recomplete differential mismatch ({path}): region count {p} vs reference {r}", fastPathName, _regions.Count, topology.CompletedRegions.Count);
				return;
			}
			HashSet<int> intSet = new HashSet<int>(topology.CompletedRegions.Count);
			for (int i = 0; i < topology.CompletedRegions.Count; i++)
			{
				intSet.Add(topology.GetRegionSpatialHash(i));
			}
			for (int j = 0; j < _regions.Count; j++)
			{
				int regionSpatialHash = GetRegionSpatialHash(j);
				if (!intSet.Contains(regionSpatialHash))
				{
					BaseTopology._logger.LogWarning("Partial-recomplete differential mismatch ({path}): region {idx} hash not present in reference", fastPathName, j);
					break;
				}
			}
		}
		catch (Exception exception)
		{
			BaseTopology._logger.LogWarning(exception, "Partial-recomplete differential validator threw");
		}
	}

	private OperationResult CompleteInternal(TopologyBuilderParams buildParams = null, TopologyBuildDiagnostics diagnostics = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (buildParams == null)
		{
			buildParams = TopologyBuilderParams.Default();
		}
		OperationResult retn = new OperationResult();
		_regions.Clear();
		bool condition = false;
		for (int i = 0; i < _arcs.Count; i++)
		{
			if (_arcs[i].StartNode == -1 || _arcs[i].EndNode == -1)
			{
				retn.AddMessage(MessageCodes.IncompleteArc.Create(i));
				retn.Result = false;
				condition = true;
			}
			_arcs[i].UnsetRegions();
			if (ArcHasInternalPointIdenticalToStartOrEndNode(i))
			{
				retn.AddMessage(MessageCodes.ArcIdenticalEndpointAndNodeIdentical.Create(i));
			}
		}
		if (condition)
		{
			ClearPartialRecompleteSnapshot();
			return retn;
		}
		if (diagnostics != null)
		{
			diagnostics.TopologyArcsExtent = GetArcExtent();
		}
		List<LineCollectionIntersection> arcSpatialIntersectionCheck = ArcSpatialIntersectionCheck(out var allArcsAsPlines);
		if (arcSpatialIntersectionCheck != null && arcSpatialIntersectionCheck.Count > 0)
		{
			for (int j = 0; j < arcSpatialIntersectionCheck.Count; j++)
			{
				LineCollectionIntersection lineCollectionIntersection = arcSpatialIntersectionCheck[j];
				int aPolylineIndex = lineCollectionIntersection.APolylineIndex;
				int bPolylineIndex = lineCollectionIntersection.BPolylineIndex;
				retn.AddMessage(MessageCodes.ArcsIntersect.Create(aPolylineIndex, bPolylineIndex));
			}
		}
		List<PointIntersection> coincidentNodeCheck = CoincidentNodeCheck();
		if (coincidentNodeCheck != null && coincidentNodeCheck.Count > 0)
		{
			for (int k = 0; k < coincidentNodeCheck.Count; k++)
			{
				PointIntersection pointIntersection = coincidentNodeCheck[k];
				int aPointIndex = pointIntersection.APointIndex;
				int bPointIndex = pointIntersection.BPointIndex;
				retn.AddMessage(MessageCodes.CoincidentNodes.Create(aPointIndex, bPointIndex));
			}
		}
		for (int l = 0; l < _nodes.Count; l++)
		{
			Node node = _nodes[l];
			if (node.Arcs == null)
			{
				node.Arcs = new List<NodeArcConnection>();
			}
			else
			{
				node.Arcs.Clear();
			}
		}
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory();
		if (diagnostics != null)
		{
			diagnostics.TinFactory = delaunayTinFactory;
		}
		for (int m = 0; m < _arcs.Count; m++)
		{
			Arc arc = _arcs[m];
			int startNode = arc.StartNode;
			int endNode = arc.EndNode;
			if (startNode != -1)
			{
				AddArcToNode(_arcs, _nodes, startNode, m, isStart: true);
			}
			if (endNode != -1)
			{
				AddArcToNode(_arcs, _nodes, endNode, m, isStart: false);
			}
			if (startNode == -1 || endNode == -1)
			{
				retn.AddMessage(MessageCodes.NullNodeArc.Create(m));
			}
			else if (startNode < 0 || startNode >= _nodes.Count || endNode < 0 || endNode >= _nodes.Count)
			{
				retn.AddMessage(MessageCodes.ArcIndexingError.Create(m));
			}
			else if (!buildParams.AllowArcToStartAndEndOnSameNode && startNode == endNode)
			{
				retn.AddMessage(MessageCodes.SelfLoopingArc.Create(m));
			}
		}
		if (!buildParams.AllowFloatingNodes)
		{
			for (int n = 0; n < _nodes.Count; n++)
			{
				if (_nodes[n].Arcs.Count == 0)
				{
					retn.AddMessage(MessageCodes.FloatingNode.Create(n));
				}
			}
		}
		if (!buildParams.AllowFloatingArcs)
		{
			for (int intervalIndex = 0; intervalIndex < Arcs.Count; intervalIndex++)
			{
				Arc arc2 = Arcs[intervalIndex];
				if (Nodes[arc2.StartNode].Arcs.Count == 1 && Nodes[arc2.EndNode].Arcs.Count == 1)
				{
					retn.AddMessage(MessageCodes.FloatingArc.Create(intervalIndex));
					retn.Result = false;
				}
				else if (Nodes[arc2.StartNode].Arcs.Count == 1 || Nodes[arc2.EndNode].Arcs.Count == 1)
				{
					retn.AddMessage(MessageCodes.HangingArc.Create(intervalIndex));
					retn.Result = false;
				}
			}
		}
		delaunayTinFactory.AddBreaklines(allArcsAsPlines);
		if (!delaunayTinFactory.HasInputs())
		{
			retn.AddMessage(MessageCodes.NoArcs.Create());
			stopwatch.Stop();
			retn.Result = false;
			ClearPartialRecompleteSnapshot();
			return retn;
		}
		HashSet<(int, int)> constrainedIndices = new HashSet<(int, int)>();
		delaunayTinFactory.OnEdgeOverConstrained = (ConstrainedEdge edgeA, ConstrainedEdge edgeB) =>
		{
			int enforcedFromIndex = edgeA.EnforcedFromIndex;
			int enforcedFromIndex2 = edgeB.EnforcedFromIndex;
			if (enforcedFromIndex < enforcedFromIndex2)
			{
				constrainedIndices.Add((enforcedFromIndex, enforcedFromIndex2));
			}
			else
			{
				constrainedIndices.Add((enforcedFromIndex2, enforcedFromIndex));
			}
		};
		Tin tin = delaunayTinFactory.ComputeTIN();
		if (diagnostics != null)
		{
			diagnostics.CompletedTin = tin;
		}
		foreach (var constrainedIndex in constrainedIndices)
		{
			retn.AddMessage(MessageCodes.ArcsOverlapping.Create(constrainedIndex.Item1, constrainedIndex.Item2));
		}
		if (retn.Messages.Count > 0)
		{
			retn.Result = false;
			ClearPartialRecompleteSnapshot();
			return retn;
		}
		BuildRegions_WalkTheTin(delaunayTinFactory, tin, retn);
		for (int intervalIndex2 = 0; intervalIndex2 < _regions.Count; intervalIndex2++)
		{
			Region region = _regions[intervalIndex2];
			if (GetRegionOuterRingPolygon(intervalIndex2).Area == 0.0)
			{
				retn.AddMessage(MessageCodes.RegionWithZeroArea.Create(intervalIndex2));
			}
			foreach (ArcWithDirection arc6 in region.OuterRing.Arcs)
			{
				Arc arc3 = Arcs[arc6.ArcID];
				if (arc6.IsForward)
				{
					arc3.RegionA = intervalIndex2;
				}
				else
				{
					arc3.RegionB = intervalIndex2;
				}
			}
			if (region.InnerRings == null)
			{
				continue;
			}
			foreach (RegionRing innerRing in region.InnerRings)
			{
				foreach (ArcWithDirection arc7 in innerRing.Arcs)
				{
					Arc arc4 = Arcs[arc7.ArcID];
					if (arc7.IsForward)
					{
						arc4.RegionA = intervalIndex2;
					}
					else
					{
						arc4.RegionB = intervalIndex2;
					}
				}
			}
		}
		if (buildParams.AllowFloatingArcs)
		{
			for (int intervalIndex3 = 0; intervalIndex3 < Arcs.Count; intervalIndex3++)
			{
				Arc arc5 = Arcs[intervalIndex3];
				if (arc5.RegionA == -1 && arc5.RegionB == -1)
				{
					Point point = Nodes[arc5.StartNode].Point;
					Point point2 = Nodes[arc5.EndNode].Point;
					for (int intervalIndex4 = 0; intervalIndex4 < _regions.Count; intervalIndex4++)
					{
						Region region2 = _regions[intervalIndex4];
						Polygon regionOuterRingPolygon = GetRegionOuterRingPolygon(intervalIndex4);
						if (!regionOuterRingPolygon.Contains(point) || !regionOuterRingPolygon.Contains(point2))
						{
							continue;
						}
						bool condition2 = true;
						if (region2.InnerRingCount > 0)
						{
							foreach (RegionRing innerRing2 in region2.InnerRings)
							{
								Polygon polygon = ComputeRingPolygon(innerRing2);
								if (polygon.Contains(point) && polygon.Contains(point2))
								{
									condition2 = false;
									break;
								}
							}
						}
						if (condition2)
						{
							arc5.RegionA = intervalIndex4;
							arc5.RegionB = intervalIndex4;
							break;
						}
					}
				}
				if (arc5.RegionA == -1 && arc5.RegionB == -1)
				{
					retn.AddMessage(MessageCodes.ExternalArc.Create(intervalIndex3));
				}
			}
		}
		stopwatch.Stop();
		retn.Result = retn.Messages.Count == 0;
		BaseTopology._logger.LogDebug("Completed the topology in {ms}ms.", stopwatch.ElapsedMilliseconds);
		if (retn.Result)
		{
			CapturePartialRecompleteSnapshot();
		}
		else
		{
			ClearPartialRecompleteSnapshot();
		}
		return retn;
		void AddArcToNode(List<Arc> arcs, List<Node> nodes, int nodeIndex, int arcIndex, bool isStart)
		{
			Node node2 = nodes[nodeIndex];
			NodeArcConnection nodeArcConnection5 = new NodeArcConnection
			{
				ArcID = arcIndex,
				IsStart = isStart
			};
			if (node2.Arcs.Count <= 1)
			{
				node2.Arcs.Add(nodeArcConnection5);
			}
			else
			{
				Arc candidate = arcs[arcIndex];
				Segment s = GetConnectionArc(nodes, candidate, nodeIndex, isStart);
				for (int intervalIndex5 = 0; intervalIndex5 < node2.Arcs.Count - 1; intervalIndex5++)
				{
					NodeArcConnection nodeArcConnection = node2.Arcs[intervalIndex5];
					NodeArcConnection nodeArcConnection2 = node2.Arcs[intervalIndex5 + 1];
					Arc candidate2 = arcs[nodeArcConnection.ArcID];
					Arc candidate3 = arcs[nodeArcConnection2.ArcID];
					Segment s2 = GetConnectionArc(nodes, candidate2, nodeIndex, nodeArcConnection.IsStart);
					Segment s3 = GetConnectionArc(nodes, candidate3, nodeIndex, nodeArcConnection2.IsStart);
					if (Segment.BearingInOrder(s2, s, s3, Orientation.CounterClockwise))
					{
						node2.Arcs.Insert(intervalIndex5 + 1, nodeArcConnection5);
						return;
					}
				}
				List<NodeArcConnection> arcs2 = node2.Arcs;
				NodeArcConnection nodeArcConnection3 = arcs2[arcs2.Count - 1];
				NodeArcConnection nodeArcConnection4 = node2.Arcs[0];
				Arc candidate4 = arcs[nodeArcConnection3.ArcID];
				Arc candidate5 = arcs[nodeArcConnection4.ArcID];
				Segment s4 = GetConnectionArc(nodes, candidate4, nodeIndex, nodeArcConnection3.IsStart);
				Segment s5 = GetConnectionArc(nodes, candidate5, nodeIndex, nodeArcConnection4.IsStart);
				if (Segment.BearingInOrder(s4, s, s5, Orientation.CounterClockwise))
				{
					node2.Arcs.Add(nodeArcConnection5);
				}
				else
				{
					retn.AddMessage(MessageCodes.ArcNodeConnectionInvalid.Create(nodeIndex));
				}
			}
		}
		static Segment GetConnectionArc(List<Node> nodes, Arc candidate, int nodeIndex, bool isStart)
		{
			Node node2 = nodes[nodeIndex];
			if (isStart)
			{
				if (candidate.InternalPoints.IsNullOrEmpty())
				{
					return new Segment(node2.Point, nodes[candidate.EndNode].Point);
				}
				return new Segment(node2.Point, candidate.InternalPoints[0]);
			}
			if (candidate.InternalPoints.IsNullOrEmpty())
			{
				return new Segment(node2.Point, nodes[candidate.StartNode].Point);
			}
			Point point3 = node2.Point;
			PointCollection internalPoints = candidate.InternalPoints;
			return new Segment(point3, internalPoints[internalPoints.Count - 1]);
		}
	}

	private void CapturePartialRecompleteSnapshot()
	{
		if (_completionCache == null)
		{
			_completionCache = new TopologyCompletionCache();
		}
		_completionCache.Capture(this);
	}

	private void ClearPartialRecompleteSnapshot()
	{
		_completionCache = null;
	}

	private bool TryFastPathSingleArcEdit(TopologyBuilderParams buildParams, TopologyBuildDiagnostics diagnostics, out OperationResult result)
	{
		result = null;
		if (_completionCache == null)
		{
			return false;
		}
		if (_arcs.Count != _completionCache.ArcCount)
		{
			return false;
		}
		int[] arcSpatialHashes = _completionCache.ArcSpatialHashes;
		int ordinal = -1;
		for (int i = 0; i < _arcs.Count; i++)
		{
			Arc arc = _arcs[i];
			if (arc.StartNode == -1 || arc.EndNode == -1)
			{
				return false;
			}
			if (GetArcSpatialHash(i) != arcSpatialHashes[i])
			{
				if (ordinal >= 0)
				{
					return false;
				}
				ordinal = i;
			}
		}
		if (ordinal >= 0)
		{
			if (diagnostics != null)
			{
				diagnostics.TopologyArcsExtent = GetArcExtent();
			}
			List<LineCollectionIntersection> arcSpatialIntersectionCheck = ArcSpatialIntersectionCheck(ordinal);
			if (arcSpatialIntersectionCheck != null && arcSpatialIntersectionCheck.Count > 0)
			{
				return false;
			}
			List<PointIntersection> coincidentNodeCheck = CoincidentNodeCheck();
			if (coincidentNodeCheck != null && coincidentNodeCheck.Count > 0)
			{
				return false;
			}
			_completionCache.UpdateArcHash(ordinal, GetArcSpatialHash(ordinal));
		}
		for (int j = 0; j < _regions.Count; j++)
		{
			_regions[j] = _regions[j].Clone();
		}
		OperationResult operationResult = new OperationResult();
		RebuildArcRegionIndexes(buildParams.AllowFloatingArcs, operationResult);
		if (ordinal >= 0)
		{
			ReassociateRegionControlPoints(operationResult, _arcs[ordinal].RegionA, _arcs[ordinal].RegionB);
		}
		else if (!RegionControlPointsStillValid())
		{
			ReassociateRegionControlPoints(operationResult);
		}
		if (operationResult.Messages.Count > 0)
		{
			return false;
		}
		operationResult.Result = true;
		result = operationResult;
		return true;
	}

	private List<LineCollectionIntersection> ArcSpatialIntersectionCheck(int arcIdx)
	{
		Arc arc = _arcs[arcIdx];
		if (arc.StartNode == -1 || arc.EndNode == -1)
		{
			return null;
		}
		Polyline arcPolyline = GetArcPolyline(arcIdx);
		List<FeatureSegmentSubset> elements = ArcSpatialIndex.GetElements(arcPolyline, dedupe: false);
		return PolylineCollection.GetIntersectionsWithLine(arcPolyline, arcIdx, elements, this, (Topology topo, int fid) => topo.GetArcPolyline(fid), includeEndptIntersections: false, 0.999, checkForIdenticalEndpoint: true);
	}

	private List<LineCollectionIntersection> ArcSpatialIntersectionCheck(out List<Polyline> allArcsAsPlines)
	{
		allArcsAsPlines = new List<Polyline>(Arcs.Count);
		for (int i = 0; i < Arcs.Count; i++)
		{
			if (_arcs[i].StartNode != -1 && _arcs[i].EndNode != -1)
			{
				Polyline arcPolyline = GetArcPolyline(i);
				allArcsAsPlines.Add(arcPolyline);
			}
		}
		return PolylineCollection.GetAllIntersections(allArcsAsPlines, ArcSpatialIndex, includeEndptIntersections: false, 0.999, checkForIdenticalEndpoint: true);
	}

	private List<PointIntersection> CoincidentNodeCheck()
	{
		double distTolerance = Math.Sqrt(NodeSpatialIndex.Extent.Area) * 1.401298464324817E-45;
		return PointCollection.GetAllCoincidentPoints(_nodes, (Node node) => node.Point, distTolerance, NodeSpatialIndex);
	}

	private Extent GetArcExtent()
	{
		Extent extent = Extent.NullExtent;
		for (int i = 0; i < Arcs.Count; i++)
		{
			Arc arc = _arcs[i];
			if (arc.StartNode != -1)
			{
				extent = Extent.Union(extent, Nodes[arc.StartNode].Point);
			}
			if (arc.EndNode != -1)
			{
				extent = Extent.Union(extent, Nodes[arc.EndNode].Point);
			}
			if (arc.InternalPoints != null)
			{
				extent = Extent.Union(extent, arc.InternalPoints);
			}
		}
		return extent;
	}

	private bool TryPartialRecomplete(TopologyBuilderParams buildParams, TopologyBuildDiagnostics diagnostics, out OperationResult result)
	{
		result = null;
		if (_completionCache == null)
		{
			return false;
		}
		int arcCount = _completionCache.ArcCount;
		int[] arcSpatialHashes = _completionCache.ArcSpatialHashes;
		List<Region> regions = _completionCache.Regions;
		if ((double)Math.Abs(_arcs.Count - arcCount) / (double)Math.Max(1, arcCount) > 0.25)
		{
			return false;
		}
		for (int i = 0; i < _arcs.Count; i++)
		{
			Arc arc = _arcs[i];
			if (arc.StartNode == -1 || arc.EndNode == -1)
			{
				return false;
			}
		}
		Dictionary<int, List<int>> intListByInt = new Dictionary<int, List<int>>(_arcs.Count);
		for (int j = 0; j < _arcs.Count; j++)
		{
			int arcSpatialHash = GetArcSpatialHash(j);
			if (!intListByInt.TryGetValue(arcSpatialHash, out var arcSpatialHashTerm))
			{
				arcSpatialHashTerm = (intListByInt[arcSpatialHash] = new List<int>(1));
			}
			arcSpatialHashTerm.Add(j);
		}
		int[] intValues = new int[arcCount];
		bool[] boolValues = new bool[_arcs.Count];
		for (int k = 0; k < arcCount; k++)
		{
			intValues[k] = -1;
		}
		for (int l = 0; l < arcCount; l++)
		{
			int key = arcSpatialHashes[l];
			if (!intListByInt.TryGetValue(key, out var localValue))
			{
				continue;
			}
			int ordinal = -1;
			int ordinal2 = 0;
			for (int m = 0; m < localValue.Count; m++)
			{
				int ordinal3 = localValue[m];
				if (!boolValues[ordinal3])
				{
					ordinal2++;
					if (ordinal < 0)
					{
						ordinal = ordinal3;
					}
				}
			}
			if (ordinal2 > 1)
			{
				return false;
			}
			if (ordinal >= 0)
			{
				intValues[l] = ordinal;
				boolValues[ordinal] = true;
			}
		}
		bool[] boolValuesArray = new bool[regions.Count];
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		for (int n = 0; n < regions.Count; n++)
		{
			Region region = regions[n];
			bool condition = true;
			if (region.OuterRing?.Arcs != null)
			{
				foreach (ArcWithDirection arc5 in region.OuterRing.Arcs)
				{
					if (arc5.ArcID < 0 || arc5.ArcID >= intValues.Length || intValues[arc5.ArcID] < 0)
					{
						condition = false;
						break;
					}
				}
			}
			if (condition && region.InnerRings != null)
			{
				for (int intervalIndex = 0; (intervalIndex < region.InnerRings.Count) & condition; intervalIndex++)
				{
					foreach (ArcWithDirection arc6 in region.InnerRings[intervalIndex].Arcs)
					{
						if (arc6.ArcID < 0 || arc6.ArcID >= intValues.Length || intValues[arc6.ArcID] < 0)
						{
							condition = false;
							break;
						}
					}
				}
			}
			if (condition && region.ContainedArcs != null)
			{
				foreach (int containedArc in region.ContainedArcs)
				{
					if (containedArc < 0 || containedArc >= intValues.Length || intValues[containedArc] < 0)
					{
						condition = false;
						break;
					}
				}
			}
			boolValuesArray[n] = condition;
			if (condition)
			{
				if (region.OuterRing?.Arcs != null)
				{
					foreach (ArcWithDirection arc7 in region.OuterRing.Arcs)
					{
						intSetSet.Add(intValues[arc7.ArcID]);
					}
				}
				if (region.InnerRings != null)
				{
					foreach (RegionRing innerRing in region.InnerRings)
					{
						foreach (ArcWithDirection arc8 in innerRing.Arcs)
						{
							intSetSet.Add(intValues[arc8.ArcID]);
						}
					}
				}
				if (region.ContainedArcs == null)
				{
					continue;
				}
				foreach (int containedArc2 in region.ContainedArcs)
				{
					intSetSet.Add(intValues[containedArc2]);
				}
				continue;
			}
			if (region.OuterRing?.Arcs != null)
			{
				foreach (ArcWithDirection arc9 in region.OuterRing.Arcs)
				{
					if (intValues[arc9.ArcID] >= 0)
					{
						intSet.Add(intValues[arc9.ArcID]);
					}
				}
			}
			if (region.InnerRings != null)
			{
				foreach (RegionRing innerRing2 in region.InnerRings)
				{
					foreach (ArcWithDirection arc10 in innerRing2.Arcs)
					{
						if (intValues[arc10.ArcID] >= 0)
						{
							intSet.Add(intValues[arc10.ArcID]);
						}
					}
				}
			}
			if (region.ContainedArcs == null)
			{
				continue;
			}
			foreach (int containedArc3 in region.ContainedArcs)
			{
				if (intValues[containedArc3] >= 0)
				{
					intSet.Add(intValues[containedArc3]);
				}
			}
		}
		List<int> intList = new List<int>();
		for (int intervalIndex2 = 0; intervalIndex2 < _arcs.Count; intervalIndex2++)
		{
			if (!boolValues[intervalIndex2])
			{
				intList.Add(intervalIndex2);
			}
		}
		HashSet<int> intSet2 = new HashSet<int>(intSet);
		foreach (int ordinal4 in intList)
		{
			intSet2.Add(ordinal4);
		}
		bool condition2 = true;
		int countOffset = regions.Count + 4;
		while (condition2 && countOffset-- > 0)
		{
			condition2 = false;
			HashSet<int> intSet3 = new HashSet<int>();
			foreach (int ordinal5 in intSet2)
			{
				Arc arc2 = _arcs[ordinal5];
				intSet3.Add(arc2.StartNode);
				intSet3.Add(arc2.EndNode);
			}
			for (int intervalIndex3 = 0; intervalIndex3 < regions.Count; intervalIndex3++)
			{
				if (!boolValuesArray[intervalIndex3])
				{
					continue;
				}
				Region region2 = regions[intervalIndex3];
				bool condition3 = false;
				if (region2.OuterRing?.Arcs != null)
				{
					foreach (ArcWithDirection arc11 in region2.OuterRing.Arcs)
					{
						int index = intValues[arc11.ArcID];
						Arc arc3 = _arcs[index];
						if (intSet3.Contains(arc3.StartNode) || intSet3.Contains(arc3.EndNode))
						{
							condition3 = true;
							break;
						}
					}
				}
				if (!condition3)
				{
					continue;
				}
				boolValuesArray[intervalIndex3] = false;
				condition2 = true;
				if (region2.OuterRing?.Arcs != null)
				{
					foreach (ArcWithDirection arc12 in region2.OuterRing.Arcs)
					{
						intSet2.Add(intValues[arc12.ArcID]);
					}
				}
				if (region2.InnerRings != null)
				{
					foreach (RegionRing innerRing3 in region2.InnerRings)
					{
						foreach (ArcWithDirection arc13 in innerRing3.Arcs)
						{
							intSet2.Add(intValues[arc13.ArcID]);
						}
					}
				}
				if (region2.ContainedArcs == null)
				{
					continue;
				}
				foreach (int containedArc4 in region2.ContainedArcs)
				{
					intSet2.Add(intValues[containedArc4]);
				}
			}
		}
		if (countOffset <= 0)
		{
			return false;
		}
		if ((double)intSet2.Count > (double)_arcs.Count * 0.75)
		{
			return false;
		}
		List<LineCollectionIntersection> arcSpatialIntersectionCheck = ArcSpatialIntersectionCheck(out var _);
		if (arcSpatialIntersectionCheck != null && arcSpatialIntersectionCheck.Count > 0)
		{
			return false;
		}
		List<PointIntersection> coincidentNodeCheck = CoincidentNodeCheck();
		if (coincidentNodeCheck != null && coincidentNodeCheck.Count > 0)
		{
			return false;
		}
		Topology topology = new Topology();
		Dictionary<int, int> globalToLocalNode = new Dictionary<int, int>();
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		Dictionary<int, int> intByInt2 = new Dictionary<int, int>();
		foreach (int ordinal6 in intSet2)
		{
			Arc arc4 = _arcs[ordinal6];
			int startNode = AddOrGetMiniNode(topology, arc4.StartNode, globalToLocalNode);
			int endNode = AddOrGetMiniNode(topology, arc4.EndNode, globalToLocalNode);
			List<Point> internalVertexes = ((arc4.InternalPointCount == 0) ? null : new List<Point>(arc4.InternalPoints));
			topology.AddArc(startNode, endNode, internalVertexes, out var fid);
			intByInt[ordinal6] = fid;
			intByInt2[fid] = ordinal6;
		}
		TopologyBuilderParams buildParams2 = new TopologyBuilderParams
		{
			AllowFloatingArcs = buildParams.AllowFloatingArcs,
			AllowFloatingNodes = buildParams.AllowFloatingNodes,
			AllowMultipleControlPointsPerRegion = buildParams.AllowMultipleControlPointsPerRegion,
			AllowZeroControlPointsInRegion = true,
			AllowArcToStartAndEndOnSameNode = buildParams.AllowArcToStartAndEndOnSameNode,
			MustBeEnclosed = false,
			AllowPartialRecomplete = false,
			DifferentialValidatePartialRecomplete = false
		};
		OperationResult operationResult;
		try
		{
			operationResult = topology.Complete(buildParams2, null, forceRecomplete: true);
		}
		catch
		{
			return false;
		}
		if (!operationResult.Result)
		{
			return false;
		}
		_regions.Clear();
		for (int intervalIndex4 = 0; intervalIndex4 < regions.Count; intervalIndex4++)
		{
			if (boolValuesArray[intervalIndex4])
			{
				_regions.Add(RemapPriorRegion(regions[intervalIndex4], intValues));
			}
		}
		foreach (Region completedRegion in topology.CompletedRegions)
		{
			Region region3 = RemapMiniRegion(completedRegion, intByInt2);
			if (region3 == null)
			{
				return false;
			}
			_regions.Add(region3);
		}
		OperationResult operationResult2 = new OperationResult();
		RebuildArcRegionIndexes(buildParams.AllowFloatingArcs, operationResult2);
		ReassociateRegionControlPoints(operationResult2);
		operationResult2.Result = operationResult2.Messages.Count == 0;
		if (operationResult2.Result)
		{
			CapturePartialRecompleteSnapshot();
		}
		else
		{
			ClearPartialRecompleteSnapshot();
		}
		result = operationResult2;
		return true;
	}

	private void RebuildArcRegionIndexes(bool allowFloatingArcs, OperationResult retn)
	{
		for (int i = 0; i < _arcs.Count; i++)
		{
			_arcs[i].UnsetRegions();
		}
		for (int j = 0; j < _regions.Count; j++)
		{
			Region region = _regions[j];
			foreach (ArcWithDirection arc4 in region.OuterRing.Arcs)
			{
				Arc arc = _arcs[arc4.ArcID];
				if (arc4.IsForward)
				{
					arc.RegionA = j;
				}
				else
				{
					arc.RegionB = j;
				}
			}
			if (region.InnerRings != null)
			{
				foreach (RegionRing innerRing in region.InnerRings)
				{
					foreach (ArcWithDirection arc5 in innerRing.Arcs)
					{
						Arc arc2 = _arcs[arc5.ArcID];
						if (arc5.IsForward)
						{
							arc2.RegionA = j;
						}
						else
						{
							arc2.RegionB = j;
						}
					}
				}
			}
			if (region.ContainedArcs == null)
			{
				continue;
			}
			foreach (int containedArc in region.ContainedArcs)
			{
				_arcs[containedArc].RegionA = (_arcs[containedArc].RegionB = j);
			}
		}
		if (!allowFloatingArcs)
		{
			return;
		}
		for (int k = 0; k < _arcs.Count; k++)
		{
			Arc arc3 = _arcs[k];
			if (arc3.RegionA != -1 || arc3.RegionB != -1)
			{
				continue;
			}
			Point point = _nodes[arc3.StartNode].Point;
			Point point2 = _nodes[arc3.EndNode].Point;
			for (int l = 0; l < _regions.Count; l++)
			{
				Polygon regionOuterRingPolygon = GetRegionOuterRingPolygon(l);
				if (!regionOuterRingPolygon.Contains(point) || !regionOuterRingPolygon.Contains(point2))
				{
					continue;
				}
				bool condition = false;
				if (_regions[l].InnerRingCount > 0)
				{
					foreach (RegionRing innerRing2 in _regions[l].InnerRings)
					{
						Polygon polygon = ComputeRingPolygon(innerRing2);
						if (polygon.Contains(point) && polygon.Contains(point2))
						{
							condition = true;
							break;
						}
					}
				}
				if (!condition)
				{
					arc3.RegionA = l;
					arc3.RegionB = l;
					break;
				}
			}
			if (arc3.RegionA == -1 && arc3.RegionB == -1)
			{
				retn.AddMessage(MessageCodes.ExternalArc.Create(k));
			}
		}
	}

	private int AddOrGetMiniNode(Topology mini, int globalNodeId, Dictionary<int, int> globalToLocalNode)
	{
		if (globalToLocalNode.TryGetValue(globalNodeId, out var localValue))
		{
			return localValue;
		}
		mini.AddNode(_nodes[globalNodeId].Point, out var fid);
		globalToLocalNode[globalNodeId] = fid;
		return fid;
	}

	private static Region RemapPriorRegion(Region prior, int[] oldToNew)
	{
		Region region = new Region();
		region.OuterRing = RemapRing(prior.OuterRing, oldToNew);
		if (prior.InnerRings != null)
		{
			region.InnerRings = new List<RegionRing>(prior.InnerRings.Count);
			foreach (RegionRing innerRing in prior.InnerRings)
			{
				region.InnerRings.Add(RemapRing(innerRing, oldToNew));
			}
		}
		if (prior.ContainedArcs != null)
		{
			region.ContainedArcs = new List<int>(prior.ContainedArcs.Count);
			foreach (int containedArc in prior.ContainedArcs)
			{
				region.ContainedArcs.Add(oldToNew[containedArc]);
			}
		}
		return region;
	}

	private static RegionRing RemapRing(RegionRing src, int[] oldToNew)
	{
		RegionRing regionRing = new RegionRing(src.Arcs.Count);
		foreach (ArcWithDirection arc in src.Arcs)
		{
			regionRing.Arcs.Add(new ArcWithDirection(oldToNew[arc.ArcID], arc.IsForward));
		}
		return regionRing;
	}

	private static Region RemapMiniRegion(Region mini, Dictionary<int, int> localToGlobalArc)
	{
		Region region = new Region();
		if (mini.OuterRing != null)
		{
			region.OuterRing = new RegionRing(mini.OuterRing.Arcs.Count);
			foreach (ArcWithDirection arc in mini.OuterRing.Arcs)
			{
				if (!localToGlobalArc.TryGetValue(arc.ArcID, out var localValue))
				{
					return null;
				}
				region.OuterRing.Arcs.Add(new ArcWithDirection(localValue, arc.IsForward));
			}
		}
		if (mini.InnerRings != null && mini.InnerRings.Count > 0)
		{
			region.InnerRings = new List<RegionRing>(mini.InnerRings.Count);
			foreach (RegionRing innerRing in mini.InnerRings)
			{
				RegionRing regionRing = new RegionRing(innerRing.Arcs.Count);
				foreach (ArcWithDirection arc2 in innerRing.Arcs)
				{
					if (!localToGlobalArc.TryGetValue(arc2.ArcID, out var localValue2))
					{
						return null;
					}
					regionRing.Arcs.Add(new ArcWithDirection(localValue2, arc2.IsForward));
				}
				region.InnerRings.Add(regionRing);
			}
		}
		if (mini.ContainedArcs != null && mini.ContainedArcs.Count > 0)
		{
			region.ContainedArcs = new List<int>(mini.ContainedArcs.Count);
			foreach (int containedArc in mini.ContainedArcs)
			{
				if (!localToGlobalArc.TryGetValue(containedArc, out var localValue3))
				{
					return null;
				}
				region.ContainedArcs.Add(localValue3);
			}
		}
		return region;
	}

	private TinSizeFunction ComputeGlobalSizeFunction()
	{
		try
		{
			OperationResult operationResult = this.BuildConceptualMesh(out var cmesh, new ConceptualMeshCache());
			if (operationResult.Result)
			{
				return cmesh.GetGlobalSizeFunction(operationResult);
			}
		}
		catch (Exception ex)
		{
			BaseTopology._logger.LogDebug("Exception while trying to build global size function {exc}", ex);
		}
		return null;
	}

	public void BuildRegions_WalkTheTin(DelaunayTinFactory tf, Tin tin, OperationResult retn)
	{
		HashSet<ArcWithDirection> visitedArcs = new HashSet<ArcWithDirection>(Arcs.Count * 2);
		List<RegionRingPackage> regionRingPackages = new List<RegionRingPackage>();
		for (int i = 0; i < Arcs.Count; i++)
		{
			SolveSingleArc(tf, tin, i, visitedArcs, out var forward, out var backward, retn);
			if (forward != null)
			{
				regionRingPackages.Add(forward);
			}
			if (backward != null)
			{
				regionRingPackages.Add(backward);
			}
		}
		List<int> intList = new List<int>();
		Dictionary<int, int> triToArcOuterRing = new Dictionary<int, int>();
		for (int j = 0; j < regionRingPackages.Count; j++)
		{
			RegionRingPackage regionRingPackage = regionRingPackages[j];
			RegionRing ring = regionRingPackage.Ring;
			if (ComputeRingPolygon(ring).Area > 0.0)
			{
				intList.Add(j);
				continue;
			}
			Region region = new Region
			{
				OuterRing = ring,
				ContainedArcs = regionRingPackage.HangingArcs?.ToList()
			};
			_regions.Add(region);
			int countOffset = _regions.Count - 1;
			triToArcOuterRing[regionRingPackage.TriangleIdx] = countOffset;
			if (region.ContainedArcs == null)
			{
				continue;
			}
			foreach (int containedArc in region.ContainedArcs)
			{
				Arcs[containedArc].RegionA = (Arcs[containedArc].RegionB = countOffset);
			}
		}
		TinFloodFillHelper tinFloodFillHelper = TinFloodFillHelper.CheckConstraints(tf);
		int ordinal = -1;
		for (int k = 0; k < tin.TriangleCount; k++)
		{
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[k];
			if (triangleDefinition.at0 == -1 || triangleDefinition.at1 == -1 || triangleDefinition.at2 == -1)
			{
				ordinal = k;
				break;
			}
		}
		if (ordinal == -1)
		{
			throw new Exception("Triangulation error.");
		}
		HashSet<int> start = tinFloodFillHelper.Start(new int[1] { ordinal }, emitSeeds: true);
		foreach (int ordinal2 in intList)
		{
			RegionRingPackage regionRingPackage2 = regionRingPackages[ordinal2];
			int triangleIdx = regionRingPackage2.TriangleIdx;
			if (!start.Contains(triangleIdx))
			{
				if (!tinFloodFillHelper.TryFind(triangleIdx, (int tt) => triToArcOuterRing.ContainsKey(tt), out var foundValue))
				{
					throw new Exception("Triangulation error");
				}
				int index = triToArcOuterRing[foundValue];
				_regions[index].AddInnerRing(regionRingPackage2.Ring);
			}
		}
		for (int ordinal3 = 0; ordinal3 < _regions.Count; ordinal3++)
		{
			AssociateRegionWithBestControlPoint(ordinal3, retn);
		}
	}

	private void SolveSingleArc(DelaunayTinFactory tf, Tin tin, int arcIdx, HashSet<ArcWithDirection> visitedArcs, out RegionRingPackage forward, out RegionRingPackage backward, OperationResult retn)
	{
		ArcWithDirection arcWithDirection = new ArcWithDirection(arcIdx, isForward: true);
		ArcWithDirection arcWithDirection2 = new ArcWithDirection(arcIdx, isForward: false);
		bool visitedArcsArcWithDirectionTerm = visitedArcs.Contains(arcWithDirection);
		bool visitedArcsArcWithDirection2Term = visitedArcs.Contains(arcWithDirection2);
		if (visitedArcsArcWithDirectionTerm & visitedArcsArcWithDirection2Term)
		{
			forward = null;
			backward = null;
			return;
		}
		if (!tf.FindConstrainedEdge(arcIdx, checkPerimEdges: false, checkBlineEdges: true, out var pointPair))
		{
			throw new StructuredException(MessageCodes.ArcNotInTriangulation.Create(arcIdx, GetArcMidpoint(arcIdx)));
		}
		Point pt = tin.Points[pointPair.Item1];
		Point pt2 = tin.Points[pointPair.Item2];
		EdgeDefinition ed = tin.FindEdge(pointPair.Item1, pointPair.Item2);
		if (ed.APointIndex != pointPair.Item1)
		{
			ed = ed.Flip();
		}
		Polyline arcPolyline = GetArcPolyline(arcIdx);
		int aTriangleIndex = ed.ATriangleIndex;
		PointOnPolyline ptOnPoly = arcPolyline.NearestPointOnPolyline(pt);
		PointOnPolyline ptOnPoly2 = arcPolyline.NearestPointOnPolyline(pt2);
		double distanceAlong = arcPolyline.DistanceAlong(ptOnPoly);
		double distanceAlong2 = arcPolyline.DistanceAlong(ptOnPoly2);
		int otherTriangle = ed.OtherTriangle(aTriangleIndex);
		TriangleDefinition triangleDefinition = tin.TriangleDefinition[aTriangleIndex];
		int e = triangleDefinition.Point012ByGlobalIdx(ed.APointIndex);
		int point012ByGlobalIdx = triangleDefinition.Point012ByGlobalIdx(ed.BPointIndex);
		bool condition = triangleDefinition.GetCounterClockwiseE012(e) == point012ByGlobalIdx;
		if (distanceAlong2 < distanceAlong)
		{
			condition = !condition;
		}
		int triangleIdx;
		int triangleIdx2;
		if (condition)
		{
			triangleIdx = aTriangleIndex;
			triangleIdx2 = otherTriangle;
		}
		else
		{
			triangleIdx = otherTriangle;
			triangleIdx2 = aTriangleIndex;
		}
		if (!visitedArcsArcWithDirectionTerm)
		{
			Tuple<RegionRing, HashSet<int>> tuple = CheckRing(triangleIdx, isForward: true);
			if (tuple == null || tuple.Item1 == null)
			{
				forward = null;
				backward = null;
				return;
			}
			forward = new RegionRingPackage();
			forward.Ring = tuple.Item1;
			forward.HangingArcs = tuple.Item2;
			forward.TriangleIdx = triangleIdx;
		}
		else
		{
			forward = null;
		}
		if (!visitedArcsArcWithDirection2Term)
		{
			Tuple<RegionRing, HashSet<int>> tuple2 = CheckRing(triangleIdx2, isForward: false);
			if (tuple2 == null || tuple2.Item1 == null)
			{
				forward = null;
				backward = null;
				return;
			}
			backward = new RegionRingPackage();
			backward.Ring = tuple2.Item1;
			backward.HangingArcs = tuple2.Item2;
			backward.TriangleIdx = triangleIdx2;
		}
		else
		{
			backward = null;
		}
		Tuple<RegionRing, HashSet<int>> CheckRing(int tIdx, bool isForward)
		{
			List<ArcWithDirection> regionArcs = FindRegionArcs(tf, tin, tIdx, ed, out var visitedHangingArcs, retn);
			if (visitedHangingArcs != null && visitedHangingArcs.Contains(arcIdx))
			{
				return null;
			}
			RegionRing regionRing;
			if (regionArcs == null)
			{
				regionRing = null;
			}
			else
			{
				regionRing = BuildRing(regionArcs, isForward);
				for (int i = 0; i < regionRing.Arcs.Count; i++)
				{
					visitedArcs.Add(regionRing.Arcs[i]);
				}
			}
			if (visitedHangingArcs != null)
			{
				foreach (int visitedHangingArc in visitedHangingArcs)
				{
					visitedArcs.Add(new ArcWithDirection(visitedHangingArc, isForward: true));
					visitedArcs.Add(new ArcWithDirection(visitedHangingArc, isForward: false));
				}
			}
			return new Tuple<RegionRing, HashSet<int>>(regionRing, visitedHangingArcs);
		}
	}

	private List<ArcWithDirection> FindRegionArcs(DelaunayTinFactory tf, Tin tin, int tIdx, EdgeDefinition ed, out HashSet<int> visitedHangingArcs, OperationResult retn)
	{
		if (!tf.IsConstrained(ed.APointIndex, ed.BPointIndex, out var _, out var constraintIdx))
		{
			throw new Exception("Triangulation error - Arc not constrained.");
		}
		visitedHangingArcs = null;
		TriangleDefinition triangleDefinition = tin.TriangleDefinition[tIdx];
		int edge012 = triangleDefinition.GetEdge012(ed.APointIndex, ed.BPointIndex);
		Dictionary<(int, int), int> visited = new Dictionary<(int, int), int>();
		triangleDefinition.EdgePoints(edge012, out var ptA, out var ptB);
		List<ArcWithDirection> arcWithDirections = new List<ArcWithDirection>();
		Segment seg = new Segment(tin.Points[ptA], tin.Points[ptB]);
		arcWithDirections.Add(new ArcWithDirection(constraintIdx, IsForward(constraintIdx, seg)));
		int tIdx2 = tIdx;
		int edge0122 = edge012;
		AddVisited(tIdx2, edge0122, constraintIdx);
		int triangleCountOffset = tin.TriangleCount + 2;
		int ordinal = 0;
		while (true)
		{
			if (ordinal++ > triangleCountOffset)
			{
				throw new Exception("Triangulation error");
			}
			RotateCandidate(tf, tin, tIdx, edge012, out var nextTIdx, out var nextE, out constraintIdx);
			tIdx = nextTIdx;
			edge012 = nextE;
			if (tIdx == tIdx2 && edge012 == edge0122)
			{
				break;
			}
			AddVisited(tIdx, edge012, constraintIdx);
			if (constraintIdx != arcWithDirections[arcWithDirections.Count - 1].ArcID)
			{
				seg = tin.GetEdgeSegment(tIdx, edge012);
				arcWithDirections.Add(new ArcWithDirection(constraintIdx, IsForward(constraintIdx, seg)));
			}
		}
		if (arcWithDirections.Count > 1)
		{
			if (arcWithDirections[0].ArcID == arcWithDirections[arcWithDirections.Count - 1].ArcID)
			{
				if (arcWithDirections[0].IsForward != arcWithDirections[arcWithDirections.Count - 1].IsForward)
				{
					retn.Result = false;
					visitedHangingArcs = null;
					return null;
				}
				arcWithDirections.RemoveAt(arcWithDirections.Count - 1);
			}
		}
		foreach (KeyValuePair<(int, int), int> visited2 in visited)
		{
			(int, int) key = visited2.Key;
			(int, int) key2 = (key.Item2, key.Item1);
			if (visited.TryGetValue(key2, out var localValue))
			{
				if (visitedHangingArcs == null)
				{
					visitedHangingArcs = new HashSet<int>();
				}
				visitedHangingArcs.Add(localValue);
			}
		}
		if (visitedHangingArcs != null)
		{
			ArcWithDirection arcWithDirection = arcWithDirections[0];
			if (visitedHangingArcs.Contains(arcWithDirection.ArcID))
			{
				arcWithDirections.Clear();
			}
			bool condition = true;
			int isForwardArcWithDirection2Term = -1;
			for (int i = 1; i < arcWithDirections.Count; i++)
			{
				ArcWithDirection arcWithDirection2 = arcWithDirections[i];
				if (visitedHangingArcs.Contains(arcWithDirection2.ArcID))
				{
					if (condition)
					{
						Arc arc = Arcs[arcWithDirection2.ArcID];
						isForwardArcWithDirection2Term = ((!arcWithDirection2.IsForward) ? arc.EndNode : arc.StartNode);
						condition = false;
						arcWithDirections.RemoveAt(i);
						i--;
					}
					else
					{
						arcWithDirections.RemoveAt(i);
						i--;
					}
				}
				else if (!condition)
				{
					Arc arc2 = Arcs[arcWithDirection2.ArcID];
					int startNode = (arcWithDirection2.IsForward ? arc2.StartNode : arc2.EndNode);
					if (isForwardArcWithDirection2Term == startNode)
					{
						condition = true;
						continue;
					}
					arcWithDirections.RemoveAt(i);
					i--;
				}
			}
		}
		return arcWithDirections;
		void AddVisited(int ordinal2, int e012, int ordinal3)
		{
			int adjacentTriangleIdx = tin.TriangleDefinition[ordinal2].AdjacentTriangleIdx(e012);
			visited.Add((ordinal2, adjacentTriangleIdx), ordinal3);
		}
	}

	private bool IsForward(int arcIdx, Segment seg)
	{
		if (Arcs[arcIdx].InternalPointCount == 0)
		{
			Segment simplifiedArcSegment = GetSimplifiedArcSegment(arcIdx);
			double fractionAlong = simplifiedArcSegment.FractionAlong(seg.A);
			double fractionAlong2 = simplifiedArcSegment.FractionAlong(seg.B);
			return fractionAlong < fractionAlong2;
		}
		Polyline arcPolyline = GetArcPolyline(arcIdx);
		for (int i = 0; i < arcPolyline.SegmentCount; i++)
		{
			Segment segment = arcPolyline.Segments[i];
			if (segment.DistanceToPointSquared(seg.A.X, seg.A.Y, out var relativePosition) <= 0.0001 && segment.DistanceToPointSquared(seg.B.X, seg.B.Y, out var relativePosition2) <= 0.0001)
			{
				if (relativePosition == RelativePosition.OnOrBeforeA && relativePosition2 == RelativePosition.OnOrAfterB)
				{
					return true;
				}
				if (relativePosition == RelativePosition.OnOrAfterB && relativePosition2 == RelativePosition.OnOrBeforeA)
				{
					return false;
				}
				double fractionAlong3 = segment.FractionAlong(seg.A);
				double fractionAlong4 = segment.FractionAlong(seg.B);
				return fractionAlong3 < fractionAlong4;
			}
		}
		throw new Exception("Segment not on ARC - Triangulation issue.");
	}

	private RegionRing BuildRing(List<ArcWithDirection> arcs, bool startForward)
	{
		return new RegionRing
		{
			Arcs = arcs
		};
	}

	private static void RotateCandidate(DelaunayTinFactory tf, Tin tin, int tIdx, int e012, out int nextTIdx, out int nextE012, out int constraintIdx)
	{
		TriangleDefinition triangleDefinition = tin.TriangleDefinition[tIdx];
		e012 = triangleDefinition.GetCounterClockwiseE012(e012);
		triangleDefinition.EdgePoints(e012, out var ptA, out var ptB);
		int ptIdx = ptA;
		if (tf.IsConstrained(ptA, ptB, out var isPerimeter, out var constraintIdx2))
		{
			nextTIdx = tIdx;
			nextE012 = e012;
			constraintIdx = constraintIdx2;
			return;
		}
		int ordinal = 0;
		int triangleCountOffset = tin.TriangleCount + 2;
		do
		{
			if (ordinal++ > triangleCountOffset)
			{
				throw new Exception("Triangulation error.");
			}
			tIdx = triangleDefinition.GetClockwiseTriangleAroundPoint(ptIdx);
			triangleDefinition = tin.TriangleDefinition[tIdx];
			e012 = triangleDefinition.GetEdge012(ptA, ptB);
			e012 = triangleDefinition.GetCounterClockwiseE012(e012);
			triangleDefinition.EdgePoints(e012, out ptA, out ptB);
		}
		while (!tf.IsConstrained(ptA, ptB, out isPerimeter, out constraintIdx2));
		nextTIdx = tIdx;
		nextE012 = e012;
		constraintIdx = constraintIdx2;
	}

	private void BuildRegions_TinFloodFill(DelaunayTinFactory tf, Tin tin, OperationResult retn, TopologyBuildDiagnostics diagnostics)
	{
		int[] triAts = new int[3];
		FloodFillHelper floodFillHelper = new FloodFillHelper((int tIdx) =>
		{
			TriangleDefinition triangleDefinition2 = tin.TriangleDefinition[tIdx];
			triAts[0] = triangleDefinition2.at0;
			triAts[1] = triangleDefinition2.at1;
			triAts[2] = triangleDefinition2.at2;
			return triAts;
		}, (int tFrom, int tTo) =>
		{
			if (tTo == -1)
			{
				return false;
			}
			TriangleDefinition triangleDefinition2 = tin.TriangleDefinition[tFrom];
			_ = tin.TriangleDefinition[tTo];
			int edge = triangleDefinition2.GetEdge012(tTo);
			triangleDefinition2.EdgePoints(edge, out var ptA, out var ptB);
			bool isPerimeter2;
			int constraintIdx2;
			return !tf.IsConstrained(ptA, ptB, out isPerimeter2, out constraintIdx2);
		});
		List<HashSet<int>> intSetList = new List<HashSet<int>>();
		BitArray bitArray = new BitArray(tin.TriangleCount);
		while (true)
		{
			int intervalIndex;
			for (intervalIndex = 0; intervalIndex < tin.TriangleCount && bitArray[intervalIndex]; intervalIndex++)
			{
			}
			if (intervalIndex == tin.TriangleCount)
			{
				break;
			}
			HashSet<int> start = floodFillHelper.Start(new int[1] { intervalIndex }, emitSeeds: true);
			bool condition = false;
			foreach (int start2 in start)
			{
				bitArray[start2] = true;
				if (tin.TriangleDefinition[start2].IsDeleted)
				{
					condition = true;
				}
			}
			if (!condition)
			{
				intSetList.Add(start);
			}
		}
		if (_regions.Capacity < intSetList.Count)
		{
			_regions.Capacity = intSetList.Count;
		}
		foreach (HashSet<int> localValue in intSetList)
		{
			HashSet<int> intSet = new HashSet<int>();
			foreach (int ordinal in localValue)
			{
				TriangleDefinition triangleDefinition = tin.TriangleDefinition[ordinal];
				if (tf.IsConstrained(triangleDefinition.p0, triangleDefinition.p1, out var isPerimeter, out var constraintIdx))
				{
					if (isPerimeter)
					{
						throw new Exception();
					}
					intSet.Add(constraintIdx);
				}
				if (tf.IsConstrained(triangleDefinition.p1, triangleDefinition.p2, out isPerimeter, out constraintIdx))
				{
					if (isPerimeter)
					{
						throw new Exception();
					}
					intSet.Add(constraintIdx);
				}
				if (tf.IsConstrained(triangleDefinition.p2, triangleDefinition.p0, out isPerimeter, out constraintIdx))
				{
					if (isPerimeter)
					{
						throw new Exception();
					}
					intSet.Add(constraintIdx);
				}
			}
			List<RegionRing> regionRings = new List<RegionRing>();
			int[] intSetArray = intSet.ToArray();
			bool[] boolValues = new bool[intSetArray.Length];
			while (true)
			{
				int ordinal2 = -1;
				for (int intervalIndex2 = 0; intervalIndex2 < intSetArray.Length; intervalIndex2++)
				{
					if (!boolValues[intervalIndex2])
					{
						ordinal2 = intervalIndex2;
						break;
					}
				}
				if (ordinal2 == -1)
				{
					break;
				}
				RegionRing regionRing = new RegionRing();
				regionRings.Add(regionRing);
				int ordinal3 = intSetArray[ordinal2];
				boolValues[ordinal2] = true;
				Arc arc = Arcs[ordinal3];
				int ordinal4 = ordinal3;
				int endNode = arc.EndNode;
				regionRing.Add(new ArcWithDirection(ordinal4, isForward: false));
				if (arc.StartNode == arc.EndNode && intSetArray.Length == 1)
				{
					continue;
				}
				for (int unity = 1; unity < intSetArray.Length; unity++)
				{
					int ordinal5 = ordinal4;
					for (int unity2 = 1; unity2 < intSetArray.Length; unity2++)
					{
						int ordinal6 = intSetArray[unity2];
						if (ordinal6 != ordinal4 && !boolValues[unity2])
						{
							Arc arc2 = Arcs[ordinal6];
							if (arc2.StartNode == endNode)
							{
								ordinal4 = ordinal6;
								endNode = arc2.EndNode;
								regionRing.Add(new ArcWithDirection(ordinal4, isForward: false));
								boolValues[unity2] = true;
								break;
							}
							if (arc2.EndNode == endNode)
							{
								ordinal4 = ordinal6;
								endNode = arc2.StartNode;
								regionRing.Add(new ArcWithDirection(ordinal4, isForward: true));
								boolValues[unity2] = true;
								break;
							}
						}
					}
					if (ordinal5 != ordinal4)
					{
						continue;
					}
					if (ordinal3 == ordinal4)
					{
						retn.AddMessage(MessageCodes.HangingArc.Create());
						break;
					}
					ArcWithDirection arcWithDirection = regionRing.Arcs[0];
					List<ArcWithDirection> arcs = regionRing.Arcs;
					ArcWithDirection arcWithDirection2 = arcs[arcs.Count - 1];
					Arc arc3 = Arcs[arcWithDirection.ArcID];
					Arc arc4 = Arcs[arcWithDirection2.ArcID];
					int startNode = (arcWithDirection.IsForward ? arc3.StartNode : arc3.EndNode);
					int endNode2 = (arcWithDirection2.IsForward ? arc4.EndNode : arc4.StartNode);
					if (startNode != endNode2)
					{
						retn.AddMessage(MessageCodes.HangingArc.Create());
					}
					break;
				}
			}
			int ordinal7 = -1;
			double minValue = double.MinValue;
			for (int intervalIndex3 = 0; intervalIndex3 < regionRings.Count; intervalIndex3++)
			{
				RegionRing regionRing2 = regionRings[intervalIndex3];
				Polygon polygon = new Polygon();
				for (int intervalIndex4 = 0; intervalIndex4 < regionRing2.Arcs.Count; intervalIndex4++)
				{
					ArcWithDirection arcWithDirection3 = regionRing2.Arcs[intervalIndex4];
					Arc arc5 = Arcs[arcWithDirection3.ArcID];
					if (arcWithDirection3.IsForward)
					{
						if (!arc5.InternalPoints.IsNullOrEmpty())
						{
							polygon.AddRange(arc5.InternalPoints);
						}
						polygon.Add(Nodes[arc5.EndNode].Point);
					}
					else
					{
						if (!arc5.InternalPoints.IsNullOrEmpty())
						{
							polygon.AddRangeReverse(arc5.InternalPoints);
						}
						polygon.Add(Nodes[arc5.StartNode].Point);
					}
				}
				if (polygon.IsClockwise())
				{
					regionRing2 = regionRing2.Reverse();
					regionRings[intervalIndex3] = regionRing2;
				}
				double absoluteArea = Math.Abs(polygon.Area);
				if (absoluteArea > minValue)
				{
					minValue = absoluteArea;
					ordinal7 = intervalIndex3;
				}
			}
			List<RegionRing> innerRings = new List<RegionRing>();
			for (int intervalIndex5 = 0; intervalIndex5 < regionRings.Count; intervalIndex5++)
			{
				if (intervalIndex5 != ordinal7)
				{
					innerRings.Add(regionRings[intervalIndex5]);
				}
			}
			Region region = new Region
			{
				OuterRing = regionRings[ordinal7],
				InnerRings = innerRings
			};
			_regions.Add(region);
			int regionFid = _regions.Count - 1;
			AssociateRegionWithBestControlPoint(regionFid, retn);
		}
	}

	private void ReassociateRegionControlPoints(OperationResult retn)
	{
		for (int i = 0; i < _regions.Count; i++)
		{
			_regions[i].ClearControlPoints();
		}
		for (int j = 0; j < _regions.Count; j++)
		{
			AssociateRegionWithBestControlPoint(j, retn);
		}
	}

	private bool RegionControlPointsStillValid()
	{
		int count = RegionControlPoints.Count;
		bool[] boolValues = new bool[count];
		int ordinal = 0;
		for (int i = 0; i < _regions.Count; i++)
		{
			IReadOnlyList<int> controlPointIndices = _regions[i].ControlPointIndices;
			if (controlPointIndices.Count > 1)
			{
				return false;
			}
			if (controlPointIndices.Count != 0)
			{
				int controlPointIndex = controlPointIndices[0];
				if (controlPointIndex < 0 || controlPointIndex >= count)
				{
					return false;
				}
				if (boolValues[controlPointIndex])
				{
					return false;
				}
				if (!RegionContainsPoint(i, RegionControlPoints[controlPointIndex]))
				{
					return false;
				}
				boolValues[controlPointIndex] = true;
				ordinal++;
			}
		}
		return ordinal == count;
	}

	private void ReassociateRegionControlPoints(OperationResult retn, int regionA, int regionB)
	{
		if (regionB == regionA)
		{
			regionB = -1;
		}
		if (regionA >= 0)
		{
			_regions[regionA].ClearControlPoints();
		}
		if (regionB >= 0)
		{
			_regions[regionB].ClearControlPoints();
		}
		if (regionA >= 0)
		{
			AssociateRegionWithBestControlPoint(regionA, retn);
		}
		if (regionB >= 0)
		{
			AssociateRegionWithBestControlPoint(regionB, retn);
		}
	}

	private void AssociateRegionWithBestControlPoint(int regionFid, OperationResult retn)
	{
		Region region = _regions[regionFid];
		List<int> elements = ControlPointSpatialIndex.GetElements(GetRegionOuterRingPolygon(regionFid).Extent, dedupe: false);
		HashSet<int> intSet = new HashSet<int>();
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			if (intSet.Contains(element))
			{
				continue;
			}
			Point pt = RegionControlPoints[element];
			if (RegionContainsPoint(regionFid, pt))
			{
				if (region.ControlPointIndex != -1)
				{
					retn.AddMessage(MessageCodes.RegionWithMultipleControlPoints.Create(regionFid));
				}
				region.TryAddControlPoint(element);
				intSet.Add(element);
			}
		}
	}

	public bool RegionContainsPoint(int rgnIdx, Point pt)
	{
		Region region = _regions[rgnIdx];
		if (GetRegionOuterRingPolygon(rgnIdx).Contains(pt))
		{
			foreach (RegionRing innerRing in region.InnerRings)
			{
				if (ComputeRingPolygon(innerRing).Contains(pt))
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	public bool TryGetControlPointContainingRegion(int controlPointIndex, out int regionIndex)
	{
		return _controlPointContainingRegionCache.TryGetValue(controlPointIndex, out regionIndex);
	}

	private Dictionary<int, int> BuildControlPointContainingRegionCache()
	{
		Dictionary<int, int> controlPointContainingRegionCache = new Dictionary<int, int>();
		for (int i = 0; i < CompletedRegions.Count; i++)
		{
			Region region = CompletedRegions[i];
			for (int j = 0; j < region.ControlPointIndices.Count; j++)
			{
				int key = region.ControlPointIndices[j];
				controlPointContainingRegionCache[key] = i;
			}
		}
		return controlPointContainingRegionCache;
	}

	public void HealControlPoints(IReadOnlyList<Region> oldTopologyRegions, OperationResult buildResult, out OperationResult newRes)
	{
		newRes = buildResult.Clone();
		Queue<HealCommand> identifyControlPointsToHeal = IdentifyControlPointsToHeal(oldTopologyRegions);
		while (identifyControlPointsToHeal.Count != 0)
		{
			HealCommand healCommand = identifyControlPointsToHeal.Dequeue();
			UpdateControlPointPoint(healCommand.ControlPointIndex, healCommand.ToPoint);
			if (healCommand.ToRegionIndex != -1)
			{
				Region region = CompletedRegions[healCommand.ToRegionIndex];
				if (region.ControlPointIndices.Count == 1)
				{
					newRes.AddMessage(MessageCodes.RegionWithMultipleControlPoints.Create(healCommand.ToRegionIndex));
				}
				region.TryAddControlPoint(healCommand.ToRegionIndex);
			}
			if (healCommand.FromRegionIndex != -1)
			{
				Region region2 = CompletedRegions[healCommand.FromRegionIndex];
				if (region2.ControlPointIndices.Count == 2)
				{
					newRes.RemoveMessage(MessageCodes.RegionWithMultipleControlPoints.Create(healCommand.FromRegionIndex));
				}
				region2.RemoveControlPoint(healCommand.FromRegionIndex);
			}
		}
		if (newRes.Messages.Count == 0)
		{
			newRes.Result = true;
		}
	}

	public Queue<HealCommand> IdentifyControlPointsToHeal(IReadOnlyList<Region> oldTopologyRegions)
	{
		ref CollectionVersionTracker arcTracker = ref _arcTracker;
		IReadOnlyList<Arc> items = _arcs;
		arcTracker.FreezeOne(in items);
		Queue<HealCommand> healCommands = new Queue<HealCommand>();
		Dictionary<int, RegionReverseLookupData> regionReverseLookupDataByInt = new Dictionary<int, RegionReverseLookupData>(oldTopologyRegions.Count);
		foreach (Region oldTopologyRegion in oldTopologyRegions)
		{
			List<ArcWithDirection> arcs = oldTopologyRegion.OuterRing.Arcs;
			int count = arcs.Count;
			for (int i = 0; i < count; i++)
			{
				int arcID = arcs[i].ArcID;
				if (!regionReverseLookupDataByInt.TryGetValue(arcID, out var localValue))
				{
					regionReverseLookupDataByInt[arcID] = new RegionReverseLookupData
					{
						RegionA = oldTopologyRegion
					};
					continue;
				}
				if (localValue.RegionB == null)
				{
					RegionReverseLookupData regionReverseLookupData = localValue;
					regionReverseLookupData.RegionB = oldTopologyRegion;
					regionReverseLookupDataByInt[arcID] = regionReverseLookupData;
					continue;
				}
				throw new InvalidOperationException("Somehow an arc is connected to more than 2 regions.");
			}
		}
		List<Region> regions = new List<Region>();
		for (int j = 0; j < CompletedRegions.Count; j++)
		{
			Region region = CompletedRegions[j];
			List<ArcWithDirection> arcs2 = region.OuterRing.Arcs;
			regions.Clear();
			int count2 = arcs2.Count;
			for (int k = 0; k < count2; k++)
			{
				if (regionReverseLookupDataByInt.TryGetValue(arcs2[k].ArcID, out var localValue2))
				{
					regions.Add(localValue2.RegionA);
					if (localValue2.RegionB != null)
					{
						regions.Add(localValue2.RegionB);
					}
				}
			}
			if (TryFindMatchingOldRegion(region, regions, out var matchingOldRegion) && matchingOldRegion.ControlPointIndices.Count == 1)
			{
				int controlPointIndex = matchingOldRegion.ControlPointIndex;
				if (controlPointIndex < RegionControlPoints.Count && !region.ControlPointIndices.Contains(controlPointIndex))
				{
					bool controlPointContainingRegion = TryGetControlPointContainingRegion(controlPointIndex, out var regionIndex);
					healCommands.Enqueue(new HealCommand
					{
						ControlPointIndex = controlPointIndex,
						FromPoint = RegionControlPoints[controlPointIndex],
						ToPoint = GetBestRegionControlPoint(j),
						ToRegionIndex = j,
						FromRegionIndex = (controlPointContainingRegion ? regionIndex : (-1))
					});
				}
			}
		}
		_arcTracker.ThawOne();
		return healCommands;
	}

	private static bool TryFindMatchingOldRegion(Region newRegion, List<Region> oldRegions, out Region matchingOldRegion)
	{
		foreach (Region oldRegion in oldRegions)
		{
			if (oldRegion.HasEqualInnerAndOuterRings(newRegion))
			{
				matchingOldRegion = oldRegion;
				return true;
			}
		}
		matchingOldRegion = null;
		return false;
	}

	public IList<int> GetDefaultOrDuplicateControlPoints(bool includeFloatingControlPoints = false)
	{
		IList<int> distinct = default(CompactList<int>);
		IList<int> intList = default(CompactList<int>);
		using (this.FreezeSafely())
		{
			for (int i = 0; i < RegionControlPoints.Count; i++)
			{
				bool controlPointContainingRegion = TryGetControlPointContainingRegion(i, out var regionIndex);
				if (controlPointContainingRegion && CompletedRegions[regionIndex].ControlPointIndices.Count <= 1)
				{
					continue;
				}
				if (!ControlPointMetadata[i].IsDefault)
				{
					if (!controlPointContainingRegion)
					{
						if (includeFloatingControlPoints)
						{
							distinct.Add(i);
						}
					}
					else
					{
						if (intList.Contains(regionIndex))
						{
							continue;
						}
						intList.Add(regionIndex);
						foreach (int duplicateControlPointsInRegion in GetDuplicateControlPointsInRegion(regionIndex))
						{
							distinct.Add(duplicateControlPointsInRegion);
						}
					}
				}
				else
				{
					distinct.Add(i);
				}
			}
			if (distinct.Count > 1)
			{
				distinct = distinct.Distinct().ToList();
			}
			return distinct;
		}
	}

	private IList<int> GetDuplicateControlPointsInRegion(int regionIndex)
	{
		IList<int> duplicateControlPointsInRegion = default(CompactList<int>);
		Region region = CompletedRegions[regionIndex];
		if (region.ControlPointIndices.Count <= 1)
		{
			return duplicateControlPointsInRegion;
		}
		for (int i = 1; i < region.ControlPointIndices.Count; i++)
		{
			duplicateControlPointsInRegion.Add(region.ControlPointIndices[i]);
		}
		return duplicateControlPointsInRegion;
	}

	public void FillRegionsWithNoControlPoints(out IList<int> addedControlPoints)
	{
		addedControlPoints = default(CompactList<int>);
		foreach (int regionsWithNoControlPoint in GetRegionsWithNoControlPoints())
		{
			AddRegionControlPoint(GetBestRegionControlPoint(regionsWithNoControlPoint), out var fid);
			CompletedRegions[regionsWithNoControlPoint].TryAddControlPoint(fid);
		}
	}

	public OperationResult Validate(bool forceComplete = false)
	{
		OperationResult operationResult = new OperationResult();
		if (forceComplete)
		{
			OperationResult other = Complete();
			operationResult.MergeIn(other);
		}
		int count = _nodes.Count;
		int count2 = _arcs.Count;
		int count3 = _regions.Count;
		int count4 = _regionControlPoints.Count;
		for (int i = 0; i < count2; i++)
		{
			Arc arc = _arcs[i];
			if (arc.StartNode < 0 || arc.StartNode >= count)
			{
				operationResult.AddFailure(MessageCodes.ArcIndexingError.Create(i));
			}
			if (arc.EndNode < 0 || arc.EndNode >= count)
			{
				operationResult.AddFailure(MessageCodes.ArcIndexingError.Create(i));
			}
			if (arc.RegionA != -1 && (arc.RegionA < 0 || arc.RegionA >= count3))
			{
				operationResult.AddFailure(MessageCodes.ArcIndexingError.Create(i));
			}
			if (arc.RegionB != -1 && (arc.RegionB < 0 || arc.RegionB >= count3))
			{
				operationResult.AddFailure(MessageCodes.ArcIndexingError.Create(i));
			}
		}
		for (int j = 0; j < count; j++)
		{
			Node node = _nodes[j];
			for (int k = 0; k < node.Arcs.Count; k++)
			{
				NodeArcConnection nodeArcConnection = node.Arcs[k];
				if (nodeArcConnection.ArcID < 0 || nodeArcConnection.ArcID >= count2)
				{
					operationResult.AddFailure(MessageCodes.NodeIndexingError.Create(j));
					continue;
				}
				Arc arc2 = _arcs[nodeArcConnection.ArcID];
				if ((nodeArcConnection.IsStart ? arc2.StartNode : arc2.EndNode) != j)
				{
					operationResult.AddFailure(MessageCodes.ArcNodeConnectionInvalid.Create(j));
				}
			}
		}
		for (int l = 0; l < count3; l++)
		{
			Region region = _regions[l];
			if (region.OuterRing != null && region.OuterRing.Arcs != null)
			{
				for (int m = 0; m < region.OuterRing.Arcs.Count; m++)
				{
					int arcID = region.OuterRing.Arcs[m].ArcID;
					if (arcID < 0 || arcID >= count2)
					{
						operationResult.AddFailure(MessageCodes.RegionIndexingError.Create(l));
					}
				}
			}
			if (region.InnerRings != null)
			{
				for (int n = 0; n < region.InnerRings.Count; n++)
				{
					RegionRing regionRing = region.InnerRings[n];
					if (regionRing?.Arcs == null)
					{
						continue;
					}
					for (int intervalIndex = 0; intervalIndex < regionRing.Arcs.Count; intervalIndex++)
					{
						int arcID2 = regionRing.Arcs[intervalIndex].ArcID;
						if (arcID2 < 0 || arcID2 >= count2)
						{
							operationResult.AddFailure(MessageCodes.RegionIndexingError.Create(l));
						}
					}
				}
			}
			if (region.ContainedArcs != null)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < region.ContainedArcs.Count; intervalIndex2++)
				{
					int containedArc = region.ContainedArcs[intervalIndex2];
					if (containedArc < 0 || containedArc >= count2)
					{
						operationResult.AddFailure(MessageCodes.RegionIndexingError.Create(l));
					}
				}
			}
			for (int intervalIndex3 = 0; intervalIndex3 < region.ControlPointIndices.Count; intervalIndex3++)
			{
				int controlPointIndex = region.ControlPointIndices[intervalIndex3];
				if (controlPointIndex < 0 || controlPointIndex >= count4)
				{
					operationResult.AddFailure(MessageCodes.RegionIndexingError.Create(l));
				}
			}
		}
		return operationResult;
	}

	public List<int> GetRegionsWithNoControlPoints()
	{
		List<int> regionsWithNoControlPoints = new List<int>();
		for (int i = 0; i < CompletedRegions.Count; i++)
		{
			if (CompletedRegions[i].ControlPointIndices.Count <= 0)
			{
				Polygon regionOuterRingPolygon = GetRegionOuterRingPolygon(i);
				if (regionOuterRingPolygon.Area == 0.0 || regionOuterRingPolygon.Points.Count == 2)
				{
					BaseTopology._logger.LogWarning("Tried to fill empty regions with control points, but found 0 area region.");
				}
				else
				{
					regionsWithNoControlPoints.Add(i);
				}
			}
		}
		return regionsWithNoControlPoints;
	}

	private void TryInitPolygonCache()
	{
		int count = CompletedRegions.Count;
		if (_regionPolygonsCache == null || _regionPolygonsCache.Length != count)
		{
			_regionPolygonsCache = new Polygon[count];
		}
		if (_regionComplexPolygonsCache == null || _regionComplexPolygonsCache.Length != count)
		{
			_regionComplexPolygonsCache = new ComplexPolygon[count];
		}
		if (_regionBestControlPointCache == null || _regionBestControlPointCache.Length != count)
		{
			_regionBestControlPointCache = new Point[count];
			_regionBestControlPointCache.Fill(Point.NullPoint);
		}
	}

	private void RebuildPolygonCache(long vxy)
	{
		if (_regionPolygonsCache.Length == CompletedRegions.Count)
		{
			Array.Fill(_regionPolygonsCache, null);
			Array.Fill(_regionComplexPolygonsCache, null);
			Array.Fill(_regionBestControlPointCache, Point.NullPoint);
		}
		else
		{
			_regionPolygonsCache = new Polygon[CompletedRegions.Count];
			_regionComplexPolygonsCache = new ComplexPolygon[CompletedRegions.Count];
			_regionBestControlPointCache = new Point[CompletedRegions.Count];
			_regionBestControlPointCache.Fill(Point.NullPoint);
			_lastCachedPolygonVersion = vxy;
		}
	}

	public Polygon GetRegionOuterRingPolygon(int rgn)
	{
		TryInitPolygonCache();
		long regionCacheVersion = GetRegionCacheVersion();
		if (_lastCachedPolygonVersion != regionCacheVersion)
		{
			RebuildPolygonCache(regionCacheVersion);
		}
		Polygon polygon = _regionPolygonsCache[rgn];
		if (polygon != null)
		{
			return polygon;
		}
		RegionRing outerRing = CompletedRegions[rgn].OuterRing;
		Polygon polygon2 = ComputeRingPolygon(outerRing);
		_regionPolygonsCache[rgn] = polygon2;
		_lastCachedPolygonVersion = regionCacheVersion;
		return polygon2;
	}

	public ComplexPolygon GetRegionComplexPolygon(int rgn)
	{
		TryInitPolygonCache();
		long regionCacheVersion = GetRegionCacheVersion();
		if (_lastCachedPolygonVersion != regionCacheVersion)
		{
			RebuildPolygonCache(regionCacheVersion);
		}
		ComplexPolygon complexPolygon = _regionComplexPolygonsCache[rgn];
		if (complexPolygon != null)
		{
			return complexPolygon;
		}
		Region region = CompletedRegions[rgn];
		RegionRing outerRing = region.OuterRing;
		PolygonContainer polygonContainer = new PolygonContainer(ComputeRingPolygon(outerRing));
		for (int i = 0; i < region.InnerRingCount; i++)
		{
			RegionRing ring = region.InnerRings[i];
			Polygon pgon = ComputeRingPolygon(ring);
			polygonContainer.AddChild(pgon);
		}
		ComplexPolygon complexPolygon2 = new ComplexPolygon(polygonContainer);
		_regionComplexPolygonsCache[rgn] = complexPolygon2;
		_lastCachedPolygonVersion = regionCacheVersion;
		return complexPolygon2;
	}

	public Point GetBestRegionControlPoint(int rgnIdx)
	{
		TryInitPolygonCache();
		long regionCacheVersion = GetRegionCacheVersion();
		if (_lastCachedPolygonVersion != regionCacheVersion)
		{
			RebuildPolygonCache(regionCacheVersion);
		}
		Point result = _regionBestControlPointCache[rgnIdx];
		if (!result.IsNull())
		{
			return result;
		}
		if (CompletedRegions[rgnIdx].InnerRings.Count == 0)
		{
			Point point = GetRegionOuterRingPolygon(rgnIdx).BestLabelPoint();
			_regionBestControlPointCache[rgnIdx] = point;
			return point;
		}
		Point bestLabelPoint = GetRegionComplexPolygon(rgnIdx).BestLabelPoint;
		_regionBestControlPointCache[rgnIdx] = bestLabelPoint;
		return bestLabelPoint;
	}

	private Polygon ComputeRingPolygon(RegionRing ring)
	{
		Polygon polygon = new Polygon();
		for (int i = 0; i < ring.Arcs.Count; i++)
		{
			ArcWithDirection arcWithDirection = ring.Arcs[i];
			Arc arc = Arcs[arcWithDirection.ArcID];
			if (arcWithDirection.IsForward)
			{
				if (!arc.InternalPoints.IsNullOrEmpty())
				{
					polygon.AddRange(arc.InternalPoints);
				}
				polygon.Add(Nodes[arc.EndNode].Point);
			}
			else
			{
				if (!arc.InternalPoints.IsNullOrEmpty())
				{
					polygon.AddRangeReverse(arc.InternalPoints);
				}
				polygon.Add(Nodes[arc.StartNode].Point);
			}
		}
		return polygon;
	}

	public List<int> GetNodesAroundRegion(int rgnId)
	{
		return CompletedRegions[rgnId].OuterRing.Arcs.Select((ArcWithDirection c) => (!c.IsForward) ? Arcs[c.ArcID].EndNode : Arcs[c.ArcID].StartNode).ToList();
	}

	public int TryFindRegionContainingPoint(Point pt)
	{
		for (int i = 0; i < _regions.Count; i++)
		{
			ComplexPolygon regionComplexPolygon = GetRegionComplexPolygon(i);
			if (!regionComplexPolygon.OuterRing.Polygon.Contains(pt))
			{
				continue;
			}
			bool condition = false;
			foreach (PolygonContainer child in regionComplexPolygon.OuterRing.Children)
			{
				if (child.Polygon.Contains(pt))
				{
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				return i;
			}
		}
		return -1;
	}

	public Topology GetRegionSubset(int rIdx)
	{
		Region region = CompletedRegions[rIdx];
		Dictionary<int, int> arcMap = new Dictionary<int, int>();
		Dictionary<int, int> nodeMap = new Dictionary<int, int>();
		Dictionary<int, int> ctrlMap = new Dictionary<int, int>();
		Topology topo = new Topology();
		topo.ArcMetadata = ArcMetadata.CloneStructure();
		topo.NodeMetadata = NodeMetadata.CloneStructure();
		topo.ControlPointMetadata = ControlPointMetadata.CloneStructure();
		foreach (ArcWithDirection arc2 in region.OuterRing.Arcs)
		{
			AddArc(arc2.ArcID);
		}
		foreach (RegionRing innerRing in region.InnerRings)
		{
			foreach (ArcWithDirection arc3 in innerRing.Arcs)
			{
				AddArc(arc3.ArcID);
			}
		}
		if (region.ControlPointIndex >= 0)
		{
			AddCtrl(region.ControlPointIndex);
		}
		topo.Complete();
		return topo;
		int AddArc(int oldIdx)
		{
			if (arcMap.TryGetValue(oldIdx, out var localValue))
			{
				return localValue;
			}
			Arc arc = Arcs[oldIdx];
			int startNode = arc.StartNode;
			int endNode = arc.EndNode;
			if (!nodeMap.TryGetValue(startNode, out var addNode))
			{
				addNode = AddNode(startNode);
			}
			if (!nodeMap.TryGetValue(endNode, out var addNode2))
			{
				addNode2 = AddNode(endNode);
			}
			TableRow other = topo.AddArc(addNode, addNode2, arc.InternalPoints.ToList());
			int arcMap2 = topo.Arcs.Count - 1;
			arcMap[oldIdx] = arcMap2;
			ArcMetadata[oldIdx].CopyTo(other, assertIdenticalTableStructure: true);
			return arcMap2;
		}
		int AddCtrl(int oldIdx)
		{
			if (ctrlMap.TryGetValue(oldIdx, out var localValue2))
			{
				return localValue2;
			}
			Point pt = RegionControlPoints[oldIdx];
			TableRow other = topo.AddRegionControlPoint(pt);
			int ctrlMap2 = topo.RegionControlPoints.Count - 1;
			ctrlMap[oldIdx] = ctrlMap2;
			ControlPointMetadata[oldIdx].CopyTo(other, assertIdenticalTableStructure: true);
			return ctrlMap2;
		}
		int AddNode(int oldIdx)
		{
			if (nodeMap.TryGetValue(oldIdx, out var localValue3))
			{
				return localValue3;
			}
			Point point = Nodes[oldIdx].Point;
			TableRow other = topo.AddNode(point);
			int nodeMap2 = topo.Nodes.Count - 1;
			nodeMap[oldIdx] = nodeMap2;
			NodeMetadata[oldIdx].CopyTo(other, assertIdenticalTableStructure: true);
			return nodeMap2;
		}
	}

	protected override Extent ComputeExtent()
	{
		Extent extent = base.ComputeExtent();
		double minX = extent.MinX;
		double maxX = extent.MaxX;
		double minY = extent.MinY;
		double maxY = extent.MaxY;
		for (int i = 0; i < RegionControlPoints.Count; i++)
		{
			Point point = RegionControlPoints[i];
			minX = Math.Min(minX, point.X);
			maxX = Math.Max(maxX, point.X);
			minY = Math.Min(minY, point.Y);
			maxY = Math.Max(maxY, point.Y);
		}
		return new Extent(minX, maxX, minY, maxY);
	}

	private SpatialIndex<int> ComputeControlPointSpatialIndex()
	{
		if (RegionControlPoints.Count == 0 || Extent.IsNull())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(SpatialIndexExtent, RegionControlPoints.Count);
		for (int i = 0; i < RegionControlPoints.Count; i++)
		{
			spatialIndex.Add(RegionControlPoints[i], i);
		}
		return spatialIndex;
	}

	private SpatialIndex<int> ComputeRegionSpatialIndex()
	{
		if (CompletedRegions.Count == 0 || Extent.IsNull())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(SpatialIndexExtent, CompletedRegions.Count);
		for (int i = 0; i < CompletedRegions.Count; i++)
		{
			spatialIndex.Add(GetRegionOuterRingPolygon(i), i);
		}
		return spatialIndex;
	}

	public override int GetDataHash()
	{
		int dataHash = base.GetDataHash();
		int combine = 0;
		for (int i = 0; i < RegionControlPoints.Count; i++)
		{
			combine = HashCode.Combine(combine, RegionControlPoints[i]);
			TableRow tableRow = ControlPointMetadata.Rows[i];
			for (int j = 0; j < tableRow.ItemArray.Length; j++)
			{
				combine = StableHash.Combine(combine, tableRow.ItemArray[j]?.GetHashCode());
			}
		}
		return StableHash.Combine(dataHash, combine);
	}

	private int HashRingSpatial(RegionRing ring)
	{
		int ordinal = 0;
		for (int i = 0; i < ring.Arcs.Count; i++)
		{
			ordinal ^= GetArcSpatialHash(ring.Arcs[i].ArcID);
		}
		return ordinal;
	}

	private int HashRingFull(RegionRing ring)
	{
		int ordinal = 0;
		for (int i = 0; i < ring.Arcs.Count; i++)
		{
			int arcID = ring.Arcs[i].ArcID;
			ordinal ^= GetArcFullHash(arcID);
		}
		return ordinal;
	}

	public int GetRegionSpatialHash(int regionId)
	{
		Region region = CompletedRegions[regionId];
		int hashRingSpatial = HashRingSpatial(region.OuterRing);
		for (int i = 0; i < region.InnerRingCount; i++)
		{
			RegionRing ring = region.InnerRings[i];
			hashRingSpatial ^= HashRingSpatial(ring);
		}
		return hashRingSpatial;
	}

	public int GetRegionFullHash(int regionId)
	{
		int hashRowBaseTopologyTerm = 0;
		Region region = CompletedRegions[regionId];
		int controlPointIndex = region.ControlPointIndex;
		if (controlPointIndex >= 0)
		{
			hashRowBaseTopologyTerm = BaseTopology.HashRow(ControlPointMetadata.Rows[controlPointIndex].ItemArray);
		}
		hashRowBaseTopologyTerm = HashCode.Combine(hashRowBaseTopologyTerm, HashRingFull(region.OuterRing));
		int ordinal = 0;
		for (int i = 0; i < region.InnerRingCount; i++)
		{
			RegionRing ring = region.InnerRings[i];
			ordinal ^= HashRingFull(ring);
		}
		return HashCode.Combine(hashRowBaseTopologyTerm, ordinal);
	}

	public override bool HitTest(Point queryPt, double nodeWorldSpaceHitDistance, double arcSegmentWorldSpaceHitDistance, double arcInternalPointWorldSpaceHitDistance, out NetworkElementType element, out int elementIndex, out int subElementIndex)
	{
		return HitTest(queryPt, HitTestSortOrder.Distance, nodeWorldSpaceHitDistance, arcSegmentWorldSpaceHitDistance, arcInternalPointWorldSpaceHitDistance, this, out element, out elementIndex, out subElementIndex);
	}

	public static bool HitTest(Point queryPt, HitTestSortOrder mode, double nodeWorldSpaceHitDistance, double arcSegmentWorldSpaceHitDistance, double arcInternalPointWorldSpaceHitDistance, Topology topology, out NetworkElementType element, out int elementIndex, out int subElementIndex)
	{
		if (topology.HitTestNode(queryPt, nodeWorldSpaceHitDistance, out elementIndex))
		{
			element = NetworkElementType.Node;
			subElementIndex = -1;
			return true;
		}
		if (topology.HitTestArc(queryPt, mode, arcSegmentWorldSpaceHitDistance, arcInternalPointWorldSpaceHitDistance, out element, out elementIndex, out subElementIndex))
		{
			return true;
		}
		subElementIndex = -1;
		return topology.HitTestRegion(queryPt, out element, out elementIndex);
	}

	public bool HitTestRegion(Point queryPt, out NetworkElementType element, out int elementIndex)
	{
		element = NetworkElementType.Region;
		List<int> elements = RegionSpatialIndex.GetElements(queryPt);
		for (int i = 0; i < elements.Count; i++)
		{
			int element2 = elements[i];
			if (RegionContainsPoint(element2, queryPt))
			{
				elementIndex = element2;
				return true;
			}
		}
		elementIndex = -1;
		return false;
	}

	public IEnumerable<int> GetRegionFidsForControlPoints(IEnumerable<int> controlPointFids)
	{
		return from ctrlPtFid in controlPointFids
			select TryGetControlPointContainingRegion(ctrlPtFid, out var regionIndex) ? regionIndex : (-1) into regionId
			where regionId >= 0
			select regionId;
	}

	public IEnumerable<int> GetArcFidsForRegions(IEnumerable<int> regionFids)
	{
		return regionFids.SelectMany((int regionFid) =>
		{
			Region region = CompletedRegions[regionFid];
			return from arc in region.OuterRing.Arcs.Union(region.InnerRings.SelectMany((RegionRing ring) => ring.Arcs))
				select arc.ArcID;
		}).Distinct();
	}

	public string DebugEmitCode()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Topology topo = new();");
		for (int i = 0; i < Nodes.Count; i++)
		{
			Point point = Nodes[i].Point;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
			handler.AppendLiteral("topo.AddNode(");
			handler.AppendFormatted(PointCtor(point));
			handler.AppendLiteral(");");
			stringBuilder3.AppendLine(ref handler);
		}
		for (int j = 0; j < Arcs.Count; j++)
		{
			Arc arc = Arcs[j];
			string textValue = "null";
			if (arc.InternalPointCount != 0)
			{
				textValue = "new[] {";
				for (int k = 0; k < arc.InternalPointCount; k++)
				{
					Point pt = arc.InternalPoints[k];
					textValue += PointCtor(pt);
					if (k != arc.InternalPointCount - 1)
					{
						textValue += ",";
					}
				}
				textValue += "}.ToList()";
			}
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(18, 3, stringBuilder2);
			handler.AppendLiteral("topo.AddArc(");
			handler.AppendFormatted(arc.StartNode);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(arc.EndNode);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(textValue);
			handler.AppendLiteral(");");
			stringBuilder4.AppendLine(ref handler);
		}
		for (int l = 0; l < RegionControlPoints.Count; l++)
		{
			Point pt2 = RegionControlPoints[l];
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(29, 1, stringBuilder2);
			handler.AppendLiteral("topo.AddRegionControlPoint(");
			handler.AppendFormatted(PointCtor(pt2));
			handler.AppendLiteral(");");
			stringBuilder5.AppendLine(ref handler);
		}
		stringBuilder.AppendLine("topo.EnsureMeshMetadataColumns();");
		for (int m = 0; m < RegionControlPoints.Count; m++)
		{
			MeshRegionMetadata controlPointData = this.GetControlPointData(m);
			float multipointControlPointDataTerm = (controlPointData.Multipoint ? 1f : 0f);
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(106, 9, stringBuilder2);
			handler.AppendLiteral("topo.SetControlPointData(");
			handler.AppendFormatted(m);
			handler.AppendLiteral(", PatchType.");
			handler.AppendFormatted(controlPointData.PatchType);
			handler.AppendLiteral(", SizeFunction.");
			handler.AppendFormatted(controlPointData.SizeFunction);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(FloatOrNaN(controlPointData.UserConstantSize));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(controlPointData.StretchRate);
			handler.AppendLiteral("f, ");
			handler.AppendFormatted(FloatOrNaN(controlPointData.UserSecondaryConstantSize));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(controlPointData.RotationDegrees);
			handler.AppendLiteral("f, multipointPrecision: ");
			handler.AppendFormatted(multipointControlPointDataTerm);
			handler.AppendLiteral("f, sizeTolerance: ");
			handler.AppendFormatted(controlPointData.SizeTolerance);
			handler.AppendLiteral("f);");
			stringBuilder6.AppendLine(ref handler);
			string replaceLineEndings = this.GetControlPointRecipeString(m).ReplaceLineEndings("\\n");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(39, 2, stringBuilder2);
			handler.AppendLiteral("topo.SetControlPointRecipeString(");
			handler.AppendFormatted(m);
			handler.AppendLiteral(", \"");
			handler.AppendFormatted(replaceLineEndings);
			handler.AppendLiteral("\");");
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(45, 2, stringBuilder2);
			handler.AppendLiteral("topo.SetControlPointTINResolution(");
			handler.AppendFormatted(m);
			handler.AppendLiteral(", (float)");
			handler.AppendFormatted(controlPointData.TINResolution);
			handler.AppendLiteral(");");
			stringBuilder8.AppendLine(ref handler);
		}
		for (int n = 0; n < Arcs.Count; n++)
		{
			ArcMetadata arcData = this.GetArcData(n);
			string message = $"ConstrainStretchingOptions.{arcData.ConstrainStretching}";
			string message2 = $"LaplaceBCOptions.{arcData.LaplaceBC}";
			string distanceStraightLineArcDataTerm = (arcData.DistanceStraightLine ? "true" : "false");
			string followExactlyArcDataTerm = (arcData.FollowExactly ? "true" : "false");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(62, 11, stringBuilder2);
			handler.AppendLiteral("topo.SetArcData(");
			handler.AppendFormatted(n);
			handler.AppendLiteral(", LinearSizeFunction.");
			handler.AppendFormatted(arcData.SizeFunction);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(FloatOrNaN(arcData.ConstantSize));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(arcData.Count);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(arcData.Bias);
			handler.AppendLiteral("f, ");
			handler.AppendFormatted(message);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(message2);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(distanceStraightLineArcDataTerm);
			handler.AppendLiteral(",");
			handler.AppendLiteral("   ");
			handler.AppendFormatted(arcData.AreaWeight);
			handler.AppendLiteral("f, ");
			handler.AppendFormatted(arcData.DiscretizeMagnetism);
			handler.AppendLiteral("f, ");
			handler.AppendFormatted(followExactlyArcDataTerm);
			handler.AppendLiteral(");");
			stringBuilder9.AppendLine(ref handler);
		}
		stringBuilder.AppendLine("var topoRes = topo.Complete();");
		stringBuilder.AppendLine("var meshRes = topo.BuildConceptualMesh(out var cmesh, out var finalMesh);");
		return stringBuilder.ToString();
		static string FloatOrNaN(float f)
		{
			if (!float.IsNaN(f))
			{
				return $"{f}f";
			}
			return "float.NaN";
		}
		static string PointCtor(Point point2)
		{
			return $"new Point({point2.X},{point2.Y})";
		}
	}

	public override void StreamIn(BinaryReader br)
	{
		base.StreamIn(br);
		_regionControlPoints.Clear();
		Point[] streamCollectionIn = br.StreamCollectionIn<Point>();
		if (streamCollectionIn != null && streamCollectionIn.Length != 0)
		{
			_regionControlPoints.AddRange(streamCollectionIn);
		}
		_regions.Clear();
		Region[] streamCollectionInArray = br.StreamCollectionIn<Region>();
		if (streamCollectionInArray != null && streamCollectionInArray.Length != 0)
		{
			_regions.AddRange(streamCollectionInArray);
		}
		this.StreamInKnownMeshMetadata(br);
		_ctrlPtVersion++;
		_globalVersion++;
	}

	public override void StreamOut(BinaryWriter bw)
	{
		base.StreamOut(bw);
		bw.StreamCollectionOut(_regionControlPoints);
		bw.StreamCollectionOut(_regions);
		this.StreamOutKnownMeshMetadata(bw);
	}

	public bool PartsAreEqual(Topology otherTopo)
	{
		if (Arcs.Count != otherTopo.Arcs.Count)
		{
			return false;
		}
		if (Nodes.Count != otherTopo.Nodes.Count)
		{
			return false;
		}
		if (RegionControlPoints.Count != otherTopo.RegionControlPoints.Count)
		{
			return false;
		}
		if (CompletedRegions.Count != otherTopo.CompletedRegions.Count)
		{
			return false;
		}
		for (int i = 0; i < Arcs.Count; i++)
		{
			if (!Arcs[i].Equals(otherTopo.Arcs[i]))
			{
				return false;
			}
		}
		for (int j = 0; j < Nodes.Count; j++)
		{
			if (!Nodes[j].Equals(otherTopo.Nodes[j]))
			{
				return false;
			}
		}
		for (int k = 0; k < RegionControlPoints.Count; k++)
		{
			if (!RegionControlPoints[k].EqualXY(otherTopo.RegionControlPoints[k]))
			{
				return false;
			}
		}
		for (int l = 0; l < CompletedRegions.Count; l++)
		{
			if (!CompletedRegions[l].Equals(otherTopo.CompletedRegions[l]))
			{
				return false;
			}
		}
		return true;
	}

	public bool IsEmpty()
	{
		if (Arcs.Count == 0 && Nodes.Count == 0 && RegionControlPoints.Count == 0)
		{
			return CompletedRegions.Count == 0;
		}
		return false;
	}

	protected override Arc BuildArc(int startNode, int endNode, List<Point> internalVertexes)
	{
		return new Arc(startNode, endNode, internalVertexes);
	}

	public override void CopyArcAttributes(int fromArcFid, int toArcFid)
	{
		ArcMetadata.SetRow(toArcFid, ArcMetadata[fromArcFid].ItemArray.ToArray());
		Arcs[toArcFid].ExternallyTracked_DateOfGenesis = Arcs[fromArcFid].ExternallyTracked_DateOfGenesis;
	}

	public Topology Clone(Topology cloneInto = null)
	{
		LogIfFrozen("Clone");
		if (cloneInto == null)
		{
			cloneInto = new Topology();
		}
		cloneInto.ResetCacheStateForClone();
		cloneInto.ArcMetadata.ClearRows();
		cloneInto.ControlPointMetadata.ClearRows();
		cloneInto.NodeMetadata.ClearRows();
		cloneInto._nodes.Clear();
		cloneInto._regions.Clear();
		cloneInto._regionControlPoints.Clear();
		cloneInto._arcs.Clear();
		for (int i = 0; i < Nodes.Count; i++)
		{
			cloneInto._nodes.Add(Nodes[i].Clone());
		}
		cloneInto._nodeVersion++;
		for (int j = 0; j < Arcs.Count; j++)
		{
			cloneInto._arcs.Add(Arcs[j].Clone());
		}
		for (int k = 0; k < RegionControlPoints.Count; k++)
		{
			cloneInto.AddRegionControlPoint(RegionControlPoints[k]);
		}
		for (int l = 0; l < CompletedRegions.Count; l++)
		{
			cloneInto._regions.Add(CompletedRegions[l].Clone());
		}
		if (ArcMetadata != null)
		{
			cloneInto.ArcMetadata = CloneTable(ArcMetadata);
		}
		if (ControlPointMetadata != null)
		{
			cloneInto.ControlPointMetadata = CloneTable(ControlPointMetadata);
		}
		if (NodeMetadata != null)
		{
			cloneInto.NodeMetadata = CloneTable(NodeMetadata);
		}
		long versionXY = cloneInto.VersionXY;
		cloneInto._globalVersion += Math.Max(VersionXY, versionXY) + 1 - versionXY;
		return cloneInto;
		static Table CloneTable(Table tbl)
		{
			Table table = tbl.CloneStructure(copyNewRowEvent: false);
			for (int m = 0; m < tbl.Rows.Count; m++)
			{
				TableRow tableRow = tbl.Rows[m];
				TableRow tableRow2 = table.AddNewRow();
				tableRow.ItemArray.CopyTo(tableRow2.ItemArray, 0);
			}
			return table;
		}
	}

	protected override void ResetCacheStateForClone()
	{
		base.ResetCacheStateForClone();
		_ctrlPtSpIdxCache.Item = null;
		_ctrlPtSpIdxCache.Version = long.MinValue;
		_regionSpIdxCache.Item = null;
		_regionSpIdxCache.Version = long.MinValue;
		_controlPointContainingRegionCacheVersioned.Item = null;
		_controlPointContainingRegionCacheVersioned.Version = long.MinValue;
		_sizeFunctionCache.Item = null;
		_sizeFunctionCache.Version = long.MinValue;
		_regionPolygonsCache = null;
		_regionComplexPolygonsCache = null;
		_regionBestControlPointCache = null;
		_lastCachedPolygonVersion = -1L;
		_lastCachedPolylineVersion = -1L;
	}

	public Topology CloneRegionAsTopology(int regionIndex, out Dictionary<int, int> arcIndexMapping, out Dictionary<int, int> nodeIndexMapping, out Dictionary<int, int> reverseArcIndexMapping, out Dictionary<int, int> reverseNodeIndexMapping)
	{
		if (regionIndex < 0 || regionIndex >= CompletedRegions.Count)
		{
			throw new ArgumentOutOfRangeException("regionIndex", "Region index out of range.");
		}
		Region region = CompletedRegions[regionIndex];
		Topology topology = new Topology();
		topology.EnsureMeshMetadataColumns();
		arcIndexMapping = new Dictionary<int, int>();
		nodeIndexMapping = new Dictionary<int, int>();
		reverseArcIndexMapping = new Dictionary<int, int>();
		reverseNodeIndexMapping = new Dictionary<int, int>();
		CloneRegionAsTopologyHelper_AddRegion(topology, region.OuterRing, arcIndexMapping, nodeIndexMapping, reverseArcIndexMapping, reverseNodeIndexMapping);
		for (int i = 0; i < region.InnerRingCount; i++)
		{
			CloneRegionAsTopologyHelper_AddRegion(topology, region.InnerRings[i], arcIndexMapping, nodeIndexMapping, reverseArcIndexMapping, reverseNodeIndexMapping);
		}
		int controlPointIndex = region.ControlPointIndex;
		if (controlPointIndex != -1)
		{
			topology.AddRegionControlPoint(RegionControlPoints[controlPointIndex]);
			topology.SetControlPointData(0, this.GetControlPointData(controlPointIndex));
		}
		return topology;
	}

	private void CloneRegionAsTopologyHelper_AddRegion(Topology newTopo, RegionRing ring, Dictionary<int, int> arcIndexMapping, Dictionary<int, int> nodeIndexMapping, Dictionary<int, int> reverseArcIndexMapping, Dictionary<int, int> reverseNodeIndexMapping)
	{
		for (int i = 0; i < ring.Arcs.Count; i++)
		{
			CloneRegionAsTopologyHelper_AddArc(newTopo, ring.Arcs[i].ArcID, arcIndexMapping, nodeIndexMapping, reverseArcIndexMapping, reverseNodeIndexMapping);
		}
	}

	private int CloneRegionAsTopologyHelper_AddArc(Topology newTopo, int arcIndex, Dictionary<int, int> arcIndexMapping, Dictionary<int, int> nodeIndexMapping, Dictionary<int, int> reverseArcIndexMapping, Dictionary<int, int> reverseNodeIndexMapping)
	{
		if (arcIndexMapping.TryGetValue(arcIndex, out var localValue))
		{
			return localValue;
		}
		Arc arc = Arcs[arcIndex];
		int startNode = CloneRegionAsTopologyHelper_AddNode(newTopo, arc.StartNode, nodeIndexMapping, reverseNodeIndexMapping);
		int endNode = CloneRegionAsTopologyHelper_AddNode(newTopo, arc.EndNode, nodeIndexMapping, reverseNodeIndexMapping);
		newTopo.AddArc(startNode, endNode, (arc.InternalPointCount == 0) ? null : new List<Point>(arc.InternalPoints), out var fid);
		arcIndexMapping[arcIndex] = fid;
		reverseArcIndexMapping[fid] = arcIndex;
		ArcMetadata arcData = this.GetArcData(arcIndex);
		newTopo.SetArcData(fid, arcData);
		return fid;
	}

	private int CloneRegionAsTopologyHelper_AddNode(Topology newTopo, int nodeIndex, Dictionary<int, int> nodeIndexMapping, Dictionary<int, int> reverseNodeIndexMapping)
	{
		if (nodeIndexMapping.TryGetValue(nodeIndex, out var localValue))
		{
			return localValue;
		}
		Node node = Nodes[nodeIndex];
		newTopo.AddNode(node.Point, out var fid);
		nodeIndexMapping[nodeIndex] = fid;
		reverseNodeIndexMapping[fid] = nodeIndex;
		return fid;
	}

	public Topology Clip(Polygon polygon, TopologyClipOptions? opts = null)
	{
		if (!opts.HasValue)
		{
			opts = TopologyClipOptions.Default();
		}
		TopologyClipOptions topologyClipOptions = opts.Value;
		Topology topology = new Topology();
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		Dictionary<int, int> intByInt2 = new Dictionary<int, int>();
		Dictionary<int, int> intByInt3 = new Dictionary<int, int>();
		for (int i = 0; i < Arcs.Count; i++)
		{
			Arc arc = Arcs[i];
			if (arc.StartNode != -1 && arc.EndNode != -1)
			{
				Polyline arcPolyline = GetArcPolyline(i);
				if (arcPolyline != null && arcPolyline.PointCount >= 2 && (topologyClipOptions.FullyContainedGeometry ? polygon.Contains(arcPolyline) : polygon.ContainsOrIntersects(arcPolyline)))
				{
					int startNode = ClipHelper_AddOrGetNode(topology, arc.StartNode, intByInt);
					int endNode = ClipHelper_AddOrGetNode(topology, arc.EndNode, intByInt);
					topology.AddArc(startNode, endNode, (arc.InternalPointCount == 0) ? null : new List<Point>(arc.InternalPoints), out var fid);
					intByInt2[i] = fid;
				}
			}
		}
		for (int j = 0; j < Nodes.Count; j++)
		{
			if (!intByInt.ContainsKey(j) && polygon.Contains(Nodes[j].Point))
			{
				ClipHelper_AddOrGetNode(topology, j, intByInt);
			}
		}
		for (int k = 0; k < RegionControlPoints.Count; k++)
		{
			Point point = RegionControlPoints[k];
			if (polygon.Contains(point))
			{
				topology.AddRegionControlPoint(point, out var fid2);
				intByInt3[k] = fid2;
			}
		}
		if (topologyClipOptions.CopyTableMetadata)
		{
			topology.NodeMetadata = ClipHelper_BuildSubsetTable(NodeMetadata, intByInt);
			topology.ArcMetadata = ClipHelper_BuildSubsetTable(ArcMetadata, intByInt2);
			topology.ControlPointMetadata = ClipHelper_BuildSubsetTable(ControlPointMetadata, intByInt3);
			topology.GlobalMetadata = ClipHelper_CloneTableShallow(GlobalMetadata);
		}
		return topology;
	}

	private int ClipHelper_AddOrGetNode(Topology newTopo, int srcNodeIdx, Dictionary<int, int> nodeIndexMapping)
	{
		if (nodeIndexMapping.TryGetValue(srcNodeIdx, out var localValue))
		{
			return localValue;
		}
		newTopo.AddNode(Nodes[srcNodeIdx].Point, out var fid);
		nodeIndexMapping[srcNodeIdx] = fid;
		return fid;
	}

	private static Table ClipHelper_BuildSubsetTable(Table source, Dictionary<int, int> srcToNewMapping)
	{
		Table table = source.CloneStructure(copyNewRowEvent: false);
		int[] intValues = new int[srcToNewMapping.Count];
		foreach (KeyValuePair<int, int> srcToNewMapping2 in srcToNewMapping)
		{
			intValues[srcToNewMapping2.Value] = srcToNewMapping2.Key;
		}
		foreach (int index in intValues)
		{
			TableRow tableRow = source.Rows[index];
			TableRow tableRow2 = table.AddNewRow();
			tableRow.ItemArray.CopyTo(tableRow2.ItemArray, 0);
		}
		return table;
	}

	private static Table ClipHelper_CloneTableShallow(Table source)
	{
		Table table = source.CloneStructure(copyNewRowEvent: false);
		for (int i = 0; i < source.Rows.Count; i++)
		{
			TableRow tableRow = source.Rows[i];
			TableRow tableRow2 = table.AddNewRow();
			tableRow.ItemArray.CopyTo(tableRow2.ItemArray, 0);
		}
		return table;
	}
}
