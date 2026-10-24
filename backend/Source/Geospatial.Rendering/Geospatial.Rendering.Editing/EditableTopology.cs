using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class EditableTopology : EditableNodeArcNetwork<Topology>
{
	private string _previewMoveControlPointVectorID = "Topo_MoveControlPoints";

	private string _previewRegionVectorID = "Topo_Region";

	private string _previewControlPointVectorID = "Topo_ControlPt";

	private string _allControlPointsVectorID = "Topo_AllControlPoints";

	public const string SplitChannelPrefix = "arc_split";

	public Topology Topology => Vector;

	public EditableTopology(Any<Topology> topology, OverlayChannels overlayChannels, IVectorManager vectorManager, Func<EditOperation, bool, bool> executeEditOperation)
		: base("Topo", "arc_split", topology, overlayChannels, vectorManager, executeEditOperation)
	{
	}

	public void PreviewMoveControlPoints(Vector deltaOffset, IEnumerable<int> ctrlPtIndices)
	{
		_overlay.TryGetVector(_previewMoveControlPointVectorID, out var id);
		VectorManager.AddOrUpdateVector(ref id, ctrlPtIndices.Select((int idx) => Vector.RegionControlPoints[idx].Translate(deltaOffset)).ToList(), DefaultEditSymbology.TopologyControlPointSymbology);
		_overlay.RegisterVector(_previewMoveControlPointVectorID, id);
	}

	public static bool CanMoveNodes(Topology topology, Vector deltaOffset, IEnumerable<int> nodeIndices, double cellSize)
	{
		return NodeArcEditGeometry.CanMoveNodes(topology, deltaOffset, nodeIndices, cellSize);
	}

	public bool TryPreviewHighlightMultipleRegionControlPoints(HitTestArgs args)
	{
		if (!Vector.HitTestRegion(args.QueryPoint, out var _, out var elementIndex))
		{
			return false;
		}
		if (Vector.CompletedRegions[elementIndex].ControlPointIndices.Count <= 1)
		{
			return false;
		}
		PreviewHighlightMultipleRegionControlPoints(elementIndex);
		return true;
	}

	public void PreviewHighlightMultipleRegionControlPoints(int regionIndex)
	{
		List<Point> points = new List<Point>();
		foreach (int controlPointIndex in Vector.CompletedRegions[regionIndex].ControlPointIndices)
		{
			Point regionControlPoint = Vector.RegionControlPoints[controlPointIndex];
			points.Add(regionControlPoint);
		}
		_overlay.TryGetVector(_previewControlPointVectorID, out var id);
		VectorManager.AddOrUpdateVector(ref id, points, DefaultEditSymbology.TopologySelectPointSymbology);
		VectorManager.SetVectorZIndex(id, 7);
		_overlay.RegisterVector(_previewControlPointVectorID, id);
	}

	public bool TryPreviewSelectRegion(HitTestArgs args)
	{
		if (!Vector.HitTestRegion(args.QueryPoint, out var _, out var elementIndex))
		{
			return false;
		}
		if (Vector.CompletedRegions[elementIndex].ControlPointIndices.Count != 1)
		{
			return false;
		}
		PreviewSelectRegion(elementIndex);
		return true;
	}

	public void PreviewSelectRegion(int regionIndex)
	{
		_overlay.TryGetVector(_previewRegionVectorID, out var id);
		VectorManager.AddOrUpdateVector(ref id, Vector.GetRegionComplexPolygon(regionIndex), DefaultEditSymbology.SelectPolygonSymbology);
		VectorManager.SetVectorZIndex(id, 7);
		_overlay.RegisterVector(_previewRegionVectorID, id);
	}

	public override bool TryPreviewMoveSelectedNodes(HitTestArgs hit, Vector delta, IEnumerable<int> nodeIndices)
	{
		if (TryPreviewMergeNodeIntoNode(hit, nodeIndices))
		{
			return true;
		}
		if (TryPreviewMergeNodeIntoArc(hit, nodeIndices))
		{
			return true;
		}
		return base.TryPreviewMoveSelectedNodes(hit, delta, nodeIndices);
	}

	public static bool CanMergeNodes(Topology topo, int removeNodeIdx, int survivingNodeIdx)
	{
		return NodeArcEditGeometry.CanMergeNodes(topo, removeNodeIdx, survivingNodeIdx);
	}

	public bool TryPreviewMergeNodeIntoNode(HitTestArgs args, IEnumerable<int> nodeIndices)
	{
		if (!NetworkRenderLayer.HitTestNode(Vector, args, out var elementIndex))
		{
			return false;
		}
		if (nodeIndices.Count() != 1)
		{
			return false;
		}
		int nodeIndicesTerm = nodeIndices.First();
		if (elementIndex == nodeIndicesTerm)
		{
			return false;
		}
		if (!CanMergeNodes(Vector, nodeIndicesTerm, elementIndex))
		{
			return false;
		}
		PreviewMergeNodeIntoNode(elementIndex, nodeIndicesTerm);
		return true;
	}

	public void PreviewMergeNodeIntoNode(int baseNode, int mergedNode)
	{
		Point point = Vector.Nodes[baseNode].Point;
		_overlay.TryGetVector(_previewAddArcVectorID, out var id);
		VectorManager.AddOrUpdateVector(ref id, point, DefaultEditSymbology.TopologyAddNodeSymbology);
		_overlay.RegisterVector(_previewAddArcVectorID, id);
		Point point2 = Vector.Nodes[mergedNode].Point;
		IReadOnlyList<Segment> nodeAttachedSegments = GetNodeAttachedSegments(mergedNode);
		_overlay.TryGetVector(_previewMoveSegmentsVectorID, out var id2);
		_overlay.TryGetVector(_previewMoveNodeVectorID, out var id3);
		EditFunctions.RenderPointWithAttachedSegmentsMoveOperation(VectorManager, ref id2, ref id3, nodeAttachedSegments, new CompactList<Point>(Vector.Nodes[mergedNode].Point), point - point2, DefaultEditSymbology.TopologyEditArcSymbology, DefaultEditSymbology.TopologyEditArcSymbology);
		_overlay.RegisterVector(_previewMoveSegmentsVectorID, id2);
		_overlay.RegisterVector(_previewMoveNodeVectorID, id3);
	}

	public static bool CanMergeNodeIntoArc(Topology topology, Point point, int nodeIndex, int hitArcIndex)
	{
		return NodeArcEditGeometry.CanMergeNodeIntoArc(topology, point, nodeIndex, hitArcIndex);
	}

	public bool TryPreviewMergeNodeIntoArc(HitTestArgs args, IEnumerable<int> nodeIndices)
	{
		if (!NetworkRenderLayer.HitTestArcs(Vector, args, out var _, out var hitArcs))
		{
			return false;
		}
		if (nodeIndices.Count() != 1)
		{
			return false;
		}
		int nodeIndicesTerm = nodeIndices.First();
		for (int i = 0; i < hitArcs.Count; i++)
		{
			if (!CanMergeNodeIntoArc(Vector, args.QueryPoint, nodeIndicesTerm, hitArcs[i].Fid))
			{
				hitArcs.RemoveAt(i);
				i--;
			}
		}
		if (hitArcs.Count == 0)
		{
			return false;
		}
		SnapResult snapResult = SnapHelpers.TrySnap(args, hitArcs, Vector.GetArcPolyline);
		if (!snapResult.Success)
		{
			return false;
		}
		PreviewMergeNodeIntoArc(snapResult.SnapPoint, snapResult.SplitLines, nodeIndicesTerm);
		return true;
	}

	public void PreviewMergeNodeIntoArc(Point snapPoint, IList<SplitLineResult> splits, int node)
	{
		EditFunctions.RenderSplitsPreview(VectorManager, _overlay, splits, Vector.GetArcPolyline, snapPoint, SplitPreviewSymbology.Polyline, "arc_split");
		Point point = Vector.Nodes[node].Point;
		IReadOnlyList<Segment> nodeAttachedSegments = GetNodeAttachedSegments(node);
		_overlay.TryGetVector(_previewMoveSegmentsVectorID, out var id);
		_overlay.TryGetVector(_previewMoveNodeVectorID, out var id2);
		EditFunctions.RenderPointWithAttachedSegmentsMoveOperation(VectorManager, ref id, ref id2, nodeAttachedSegments, new CompactList<Point>(Vector.Nodes[node].Point), snapPoint - point, DefaultEditSymbology.TopologyEditArcSymbology, DefaultEditSymbology.TopologyEditNodeSymbology);
		_overlay.RegisterVector(_previewMoveSegmentsVectorID, id);
		_overlay.RegisterVector(_previewMoveNodeVectorID, id2);
	}

	public void PreviewAllControlPoints(List<Point> scratchSpace = null, IList<int> blacklist = null)
	{
		if (!TopologyEditingDefaults.DebugShowAllControlPoints)
		{
			return;
		}
		if (scratchSpace == null)
		{
			scratchSpace = new List<Point>();
		}
		using (Vector.FreezeSafely())
		{
			scratchSpace.Clear();
			for (int i = 0; i < Vector.RegionControlPoints.Count; i++)
			{
				if (blacklist == null || !blacklist.Contains(i))
				{
					if (!Vector.TryGetControlPointContainingRegion(i, out var regionIndex))
					{
						scratchSpace.Add(Vector.RegionControlPoints[i]);
					}
					else if (TopologyEditingDefaults.DebugShowAllControlPoints || Vector.CompletedRegions[regionIndex].ControlPointIndices.Count > 1)
					{
						scratchSpace.Add(Vector.RegionControlPoints[i]);
					}
				}
			}
			_overlay.TryGetVector(_allControlPointsVectorID, out var id);
			VectorManager.AddOrUpdateVector(ref id, scratchSpace, DefaultEditSymbology.TopologyControlPointSymbology);
			_overlay.RegisterVector(_allControlPointsVectorID, id);
		}
	}

	public void SetPreviewAllSymbology(VectorSymbology arcSymbology, VectorSymbology nodeSymbology, VectorSymbology ctrlPtSymbology)
	{
		SetPreviewAllSymbology(arcSymbology, nodeSymbology);
		VectorID vector = _overlay.GetVector(_allControlPointsVectorID);
		if (!vector.IsNull())
		{
			VectorManager.TryUpdateSymbology(vector, ctrlPtSymbology);
		}
	}
}
