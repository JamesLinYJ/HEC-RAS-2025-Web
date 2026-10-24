using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class DeleteControlPointsOperation : TopologyEditOperation
{
	private IList<int> _ctrlPtIndices;

	private HashSet<int> _arcIndicesToDelete = new HashSet<int>();

	private HashSet<int> _nodesToDelete = new HashSet<int>();

	private HashSet<int> _regionsThatStartedWithMultipleCtrlPts = new HashSet<int>();

	private const int _emptyIndex = -1;

	public override string Description => "Delete Control Point";

	public DeleteControlPointsOperation(TopologyEditContext context, IList<int> ctrlPtIndices)
		: base(context, null)
	{
		_ctrlPtIndices = new List<int>(ctrlPtIndices.OrderByDescending((int i) => i));
		IdentifyArcsToDelete();
	}

	protected override bool ApplyInternal()
	{
		ClearAllSelection();
		_deletedControlPoints.Clear();
		DeleteArcsOperation.DeleteArcs(_arcIndicesToDelete, _context, in _deletedArcs, in _deletedNodes, (Stack<SavedControlPoint>)null, tryDeleteEndingNodes: false, tryDeletingDupedControlPoints: false);
		DeleteNodesOperation.TryDeleteNodes(_nodesToDelete, _context, in _deletedNodes, in _deletedArcs, combineArcsIfOnlyTwoConnectToNode: false);
		foreach (int ctrlPtIndex in _ctrlPtIndices)
		{
			DeleteControlPoint(ctrlPtIndex, alertControlPointDeleted: false);
		}
		_context.OnControlPointsRemoved(_ctrlPtIndices);
		return true;
	}

	private void DeleteControlPoint(int ctrlPtFid, bool alertControlPointDeleted = true)
	{
		DeleteControlPoint(_context, ctrlPtFid, in _deletedControlPoints, alertControlPointDeleted);
	}

	public static void DeleteControlPoint(TopologyEditContext context, int ctrlPtFid, in Stack<SavedControlPoint> deletedControlPoints, bool alertControlPointDeleted = true)
	{
		Point originalPoint = context.Topology.RegionControlPoints[ctrlPtFid];
		object[] metaDataRow = context.Topology.ControlPointMetadata.CloneRow(ctrlPtFid);
		context.Topology.RemoveRegionControlPoint(ctrlPtFid);
		deletedControlPoints.Push(new SavedControlPoint(ctrlPtFid, originalPoint, metaDataRow));
		if (alertControlPointDeleted)
		{
			context.OnControlPointsRemoved(new CompactList<int>(ctrlPtFid));
		}
	}

	private void IdentifyArcsToDelete()
	{
		HashSet<int> regionIndexesThatWillNoLongerExist = new HashSet<int>();
		_context.Topology.FreezeOne();
		for (int i = 0; i < _ctrlPtIndices.Count; i++)
		{
			int controlPointIndex = _ctrlPtIndices[i];
			if (_context.Topology.TryGetControlPointContainingRegion(controlPointIndex, out var regionIndex))
			{
				Region region = _context.Topology.CompletedRegions[regionIndex];
				bool regionsThatStartedWithMultipleCtrlPtsTerm = _regionsThatStartedWithMultipleCtrlPts.Contains(regionIndex);
				if (!regionsThatStartedWithMultipleCtrlPtsTerm && region.ControlPointIndices.Count > 1)
				{
					regionsThatStartedWithMultipleCtrlPtsTerm = true;
					_regionsThatStartedWithMultipleCtrlPts.Add(regionIndex);
				}
				if (!regionsThatStartedWithMultipleCtrlPtsTerm && TryIdentifyRegionArcsToDelete(regionIndex, regionIndexesThatWillNoLongerExist))
				{
					i = 0;
				}
			}
		}
		_context.Topology.ThawOne();
	}

	private bool TryIdentifyRegionArcsToDelete(int regionIndex, HashSet<int> regionIndexesThatWillNoLongerExist)
	{
		if (IdentifyRegionArcsToDelete(regionIndex, -1, regionIndexesThatWillNoLongerExist, in _arcIndicesToDelete, in _nodesToDelete))
		{
			return true;
		}
		_ = _context.Topology.CompletedRegions[regionIndex];
		return false;
	}

	private bool IdentifyRegionArcsToDelete(int regionIndex, int emptyIndex, HashSet<int> regionIndexesThatWillNoLongerExist, in HashSet<int> arcIndicesToDelete, in HashSet<int> nodesToDelete)
	{
		Region region = _context.Topology.CompletedRegions[regionIndex];
		bool result = false;
		foreach (ArcWithDirection arc2 in region.OuterRing.Arcs)
		{
			int arcID = arc2.ArcID;
			if (arcIndicesToDelete.Contains(arcID))
			{
				continue;
			}
			Arc arc = _context.Topology.Arcs[arcID];
			if (arc.RegionA == emptyIndex || arc.RegionB == emptyIndex)
			{
				arcIndicesToDelete.Add(arcID);
				nodesToDelete.Add(arc.StartNode);
				nodesToDelete.Add(arc.EndNode);
				regionIndexesThatWillNoLongerExist.Add(regionIndex);
				result = true;
				continue;
			}
			if ((arc.RegionA != regionIndex && regionIndexesThatWillNoLongerExist.Contains(arc.RegionA)) || (arc.RegionB != regionIndex && regionIndexesThatWillNoLongerExist.Contains(arc.RegionB)))
			{
				arcIndicesToDelete.Add(arcID);
				nodesToDelete.Add(arc.StartNode);
				nodesToDelete.Add(arc.EndNode);
				regionIndexesThatWillNoLongerExist.Add(regionIndex);
				result = true;
				continue;
			}
			_ = _context.Topology.CompletedRegions[arc.RegionA];
			bool condition = false;
			if (arc.RegionA != -1)
			{
				foreach (RegionRing innerRing in _context.Topology.CompletedRegions[arc.RegionA].InnerRings)
				{
					if (condition)
					{
						break;
					}
					if (innerRing.ContainsArc(arc2.ArcID))
					{
						condition = true;
					}
				}
			}
			if (arc.RegionB != -1)
			{
				foreach (RegionRing innerRing2 in _context.Topology.CompletedRegions[arc.RegionB].InnerRings)
				{
					if (condition)
					{
						break;
					}
					if (innerRing2.ContainsArc(arc2.ArcID))
					{
						condition = true;
					}
				}
			}
			if (condition)
			{
				arcIndicesToDelete.Add(arcID);
				nodesToDelete.Add(arc.StartNode);
				nodesToDelete.Add(arc.EndNode);
				regionIndexesThatWillNoLongerExist.Add(regionIndex);
				result = true;
			}
		}
		return result;
	}

	public void UndoDeleteControlPoints()
	{
		UndoDeleteControlPoints(_context, _deletedControlPoints);
	}

	public static void UndoDeleteControlPoints(TopologyEditContext context, Stack<SavedControlPoint> deletedCtrlPts)
	{
		if (deletedCtrlPts != null)
		{
			while (deletedCtrlPts.Count > 0)
			{
				SavedControlPoint data = deletedCtrlPts.Pop();
				UndoDeleteControlPoint(context, data);
			}
		}
	}

	public static void UndoDeleteControlPoint(TopologyEditContext context, SavedControlPoint data, bool alertControlPointAdded = true)
	{
		context.Topology.InsertRegionControlPoint(data.FID, data.OriginalPoint);
		context.Topology.ControlPointMetadata.TrySetRow(data.FID, data.MetaDataRow);
		if (alertControlPointAdded)
		{
			context.OnControlPointsInserted(new CompactList<int>(data.FID));
		}
	}

	protected override bool UndoInternal()
	{
		DeleteNodesOperation.UndoDeleteNodes(_deletedNodes, _context);
		DeleteArcsOperation.UndoDeleteArcs(_deletedArcs, _deletedControlPoints, _context);
		UndoDeleteControlPoints();
		UndoSelectionState();
		return true;
	}
}
