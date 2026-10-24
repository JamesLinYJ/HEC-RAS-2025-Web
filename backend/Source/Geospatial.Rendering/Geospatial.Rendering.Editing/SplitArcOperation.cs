using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class SplitArcOperation : NodeArcEditOperation
{
	public struct SplitArcResult
	{
		public int AddedArcFID;

		public SplitLineResult SplitLineResult;

		public Point BeforeSnappedVerticeLocation;

		public Point AfterSnappedVerticeLocation;

		public int OriginalArcFID => SplitLineResult.FID;

		public bool SplitLineWasOnInternalPoint => SplitLineResult.IsSnappedToVertice;
	}

	private CompactList<SplitArcResult> _splitArcs;

	private int _newNodeFID;

	public override string Description => "Split Arc";

	public SplitArcOperation(NodeArcEditContext context, HitTestArgs args)
		: base(context, args)
	{
	}

	protected override bool ApplyInternal()
	{
		Point queryPoint = _location.QueryPoint;
		if (!Context.Network.HitTestArcs(queryPoint, 5.0 * _location.Window.CellSize, 5.0 * _location.Window.CellSize, out var _, out var hitElements))
		{
			return false;
		}
		ClearAllSelection();
		if (!SplitArc(Context.Network, _location, hitElements, Context.AllowOverlappingArcSplits, out var addedNodeFID, out _splitArcs))
		{
			return false;
		}
		Context.OnNodeAdded(addedNodeFID);
		NotifyArcsAdded(Context, _splitArcs);
		_newNodeFID = addedNodeFID;
		return true;
	}

	public static void NotifyArcsAdded(NodeArcEditContext context, CompactList<SplitArcResult> splitArcs)
	{
		foreach (SplitArcResult orderBy in splitArcs.OrderBy((SplitArcResult res) => res.AddedArcFID))
		{
			context.OnArcAdded(orderBy.AddedArcFID);
		}
	}

	public static bool CanSplit(BaseTopology network, PointOnPolyline pop, int arcIndex)
	{
		if (pop.SegmentIndex == 0 && pop.Fraction == 0.0)
		{
			return false;
		}
		int internalPointCountOffset = network.Arcs[arcIndex].InternalPointCount + 1;
		if (pop.SegmentIndex == internalPointCountOffset - 1 && pop.Fraction == 1.0)
		{
			return false;
		}
		return true;
	}

	public static bool SplitArc(BaseTopology network, HitTestArgs location, IList<FeatureSegmentSubset> arcs, bool resplitSameArc, out int addedNodeFID, out CompactList<SplitArcResult> splitArcs)
	{
		addedNodeFID = -1;
		splitArcs = default;
		SnapResult snapResult = SnapHelpers.TrySnap(location, arcs, network.GetArcPolyline);
		SnapMode result = snapResult.Result;
		if (result == SnapMode.NoSnap || result == SnapMode.FailedLineIntersection)
		{
			return false;
		}
		CompactList<SplitLineResult> splitLines = snapResult.SplitLines;
		if (resplitSameArc && splitLines.Count > 2)
		{
			throw new NotImplementedException("Currently doesn't support more than 2 split lines.");
		}
		bool condition = false;
		HashSet<int> intSet = (resplitSameArc ? null : new HashSet<int>());
		for (int i = 0; i < splitLines.Count; i++)
		{
			SplitLineResult splitLineResult = splitLines[i];
			if (resplitSameArc)
			{
				if (i > 0 && splitLines[i - 1].FID == splitLineResult.FID)
				{
					SplitArcResult splitArcResult = splitArcs[splitArcs.Count - 1];
					int count = splitArcResult.SplitLineResult.SnappedVertices.Count;
					int internalPointCountPlusCount = network.Arcs[splitLineResult.FID].InternalPointCount + count;
					int addedArcFID = splitArcResult.AddedArcFID;
					splitLineResult.FID = addedArcFID;
					PointOnPolyline point = splitLineResult.Point;
					point.SegmentIndex = splitLineResult.Point.SegmentIndex - internalPointCountPlusCount;
					splitLineResult.Point = point;
					if (splitLineResult.IsSnappedToVertice)
					{
						for (int j = 0; j < splitLineResult.SnappedVertices.Count; j++)
						{
							int snappedVertice = splitLineResult.SnappedVertices[j];
							if (count != 0 && splitArcResult.SplitLineResult.SnappedVertices.Contains(snappedVertice))
							{
								splitLineResult.SnappedVertices.RemoveAt(j);
								j--;
							}
							else
							{
								splitLineResult.SnappedVertices[j] -= internalPointCountPlusCount;
							}
						}
					}
					splitLines[i] = splitLineResult;
				}
			}
			else if (intSet.Contains(splitLineResult.FID))
			{
				continue;
			}
			int fID = splitLineResult.FID;
			if (CanSplit(network, splitLineResult.Point, fID))
			{
				if (!condition)
				{
					network.AddNode(splitLineResult.Point.Point, out addedNodeFID);
					condition = true;
				}
				splitArcs.Add(SplitArc(network, splitLineResult, addedNodeFID));
				intSet?.Add(fID);
			}
		}
		return splitArcs.Count > 0;
	}

	private static List<Point> GetInternalPointRange(BaseTopology network, int arcFid, int start, int count)
	{
		List<Point> points = new List<Point>(count);
		for (int i = 0; i < count; i++)
		{
			points.Add(network.Arcs[arcFid].InternalPoints[start + i]);
		}
		return points;
	}

	private static SplitArcResult SplitArc(BaseTopology network, SplitLineResult lineSplit, int newNodeFid)
	{
		PointOnPolyline point = lineSplit.Point;
		int fID = lineSplit.FID;
		int internalPointCount = network.Arcs[fID].InternalPointCount;
		Point beforeSnappedVerticeLocation = Point.NullPoint;
		Point afterSnappedVerticeLocation = Point.NullPoint;
		List<Point> points;
		List<Point> internalVertexes;
		if (internalPointCount == 0)
		{
			points = new List<Point>();
			internalVertexes = new List<Point>();
		}
		else
		{
			int start = 0;
			int segmentIndex = point.SegmentIndex;
			int segmentIndex2 = segmentIndex;
			int segmentIndex3 = segmentIndex;
			int internalPointCountAboveSegmentIndex = internalPointCount - point.SegmentIndex;
			if (lineSplit.IsSnappedToVertice)
			{
				bool condition = false;
				bool condition2 = false;
				for (int i = 0; i < lineSplit.SnappedVertices.Count; i++)
				{
					int snappedVerticesLineSplitTerm = lineSplit.SnappedVertices[i] - 1;
					if (snappedVerticesLineSplitTerm >= 0 && snappedVerticesLineSplitTerm < internalPointCount)
					{
						if (snappedVerticesLineSplitTerm < segmentIndex2)
						{
							condition = true;
						}
						else if (snappedVerticesLineSplitTerm >= segmentIndex2)
						{
							condition2 = true;
						}
					}
				}
				if (condition)
				{
					beforeSnappedVerticeLocation = network.Arcs[fID].InternalPoints[segmentIndex - 1];
					segmentIndex2--;
				}
				if (condition2)
				{
					afterSnappedVerticeLocation = network.Arcs[fID].InternalPoints[segmentIndex];
					segmentIndex3++;
					internalPointCountAboveSegmentIndex--;
				}
			}
			points = GetInternalPointRange(network, fID, start, segmentIndex2);
			internalVertexes = GetInternalPointRange(network, fID, segmentIndex3, internalPointCountAboveSegmentIndex);
		}
		network.AddArc(newNodeFid, network.Arcs[fID].EndNode, internalVertexes, out var fid);
		network.CopyArcAttributes(fID, fid);
		network.Arcs[fID].SetPoints(points);
		network.ReconnectArc(fID, newNodeFid, isStart: false);
		return new SplitArcResult
		{
			AddedArcFID = fid,
			SplitLineResult = lineSplit,
			BeforeSnappedVerticeLocation = beforeSnappedVerticeLocation,
			AfterSnappedVerticeLocation = afterSnappedVerticeLocation
		};
	}

	public static bool UnsplitArcs(BaseTopology network, IList<SplitArcResult> splitArcs, int addedNodeFID, Action<int> arcRemovedCallback = null)
	{
		foreach (SplitArcResult orderByDescending in splitArcs.OrderByDescending((SplitArcResult splitArcResult) => splitArcResult.AddedArcFID))
		{
			int originalArcFID = orderByDescending.OriginalArcFID;
			int addedArcFID = orderByDescending.AddedArcFID;
			List<Point> internalPointRange = GetInternalPointRange(network, originalArcFID, 0, network.Arcs[originalArcFID].InternalPointCount);
			if (orderByDescending.SplitLineWasOnInternalPoint)
			{
				Point beforeSnappedVerticeLocation = orderByDescending.BeforeSnappedVerticeLocation;
				if (!beforeSnappedVerticeLocation.IsNull())
				{
					internalPointRange.Add(orderByDescending.BeforeSnappedVerticeLocation);
				}
				beforeSnappedVerticeLocation = orderByDescending.AfterSnappedVerticeLocation;
				if (!beforeSnappedVerticeLocation.IsNull())
				{
					internalPointRange.Add(orderByDescending.AfterSnappedVerticeLocation);
				}
			}
			int internalPointCount = network.Arcs[addedArcFID].InternalPointCount;
			for (int intervalIndex = 0; intervalIndex < internalPointCount; intervalIndex++)
			{
				internalPointRange.Add(network.Arcs[addedArcFID].InternalPoints[intervalIndex]);
			}
			network.Arcs[originalArcFID].SetPoints(internalPointRange);
			network.ReconnectArc(originalArcFID, network.Arcs[addedArcFID].EndNode, isStart: false);
			network.RemoveArc(addedArcFID);
			arcRemovedCallback?.Invoke(addedArcFID);
		}
		network.RemoveNode(addedNodeFID);
		return true;
	}

	protected override bool UndoInternal()
	{
		UndoSelectionState();
		if (!UnsplitArcs(Context.Network, _splitArcs, _newNodeFID, Context.OnArcRemoved))
		{
			return false;
		}
		Context.OnNodeRemoved(_newNodeFID);
		return true;
	}
}
