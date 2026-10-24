using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class EditableArc : GenericEditableLine<ArcPolylineWrapper>
{
	public enum EditingMode
	{
		Adding,
		Editing
	}

	public enum SnapMode
	{
		AppendSnap,
		MoveSnap
	}

	public const string SnapChannelPrefix = "arc_snap";

	private int _arcIndex;

	private Any<BaseTopology> _networkAny;

	private EditingMode _editingMode;

	private List<int> _previewScratchSpaceList = new List<int>(1);

	public BaseArc Arc => _network.Arcs[_arcIndex];

	public int ArcFID => _arcIndex;

	private BaseTopology _network => _networkAny.Value;

	public EditingMode Mode => _editingMode;

	public override ArcPolylineWrapper Vector => new ArcPolylineWrapper(_networkAny, ArcFID);

	protected override long VersionXY => Arc.VersionXY;

	public EditableArc(int arcIndex, Any<BaseTopology> network, EditingMode mode, IVectorManager vectorManager, Func<EditOperation, bool, bool> executeEditOperation, SelectedSet pointSelectionCache, OverlayChannels overlayChannels)
		: base(default(ArcPolylineWrapper), vectorManager, VectorType.Line, executeEditOperation, pointSelectionCache, overlayChannels)
	{
		_networkAny = network;
		_arcIndex = arcIndex;
		_editingMode = mode;
	}

	public override void DrawSegmentsAndPoints(int start = 0, int end = int.MaxValue, bool forceRedraw = false)
	{
		if (_lastInvalidatedVersion != VersionXY || forceRedraw)
		{
			_lastInvalidatedVersion = VersionXY;
			if (start == 0 && end == int.MaxValue)
			{
				VectorManager.AddOrUpdateVector(ref _openFeatureSegmentsVectorId, Vector, DefaultEditSymbology.TopologyEditArcSymbology, VectorType.Line);
			}
			else
			{
				VectorManager.AddOrUpdateVector(ref _openFeatureSegmentsVectorId, Vector.Skip(start).Take(end + 1).ToList(), DefaultEditSymbology.TopologyEditArcSymbology, VectorType.Line);
			}
			VectorManager.AddOrUpdateVector(ref _openFeaturePointsVectorId, Vector.Skip(start + 1).Take(end).ToList(), DefaultEditSymbology.TopologyArcInternalPointSymbology);
		}
	}

	public bool CanSplitArcAtPoint(Point point, int splitArcIndex)
	{
		return NodeArcEditGeometry.CanSplitArcAtPoint(_network, ArcFID, point, splitArcIndex);
	}

	public bool CanFinishAddingAtNode(int nodeIndex)
	{
		return NodeArcEditGeometry.CanFinishAddingAtNode(_network, ArcFID, nodeIndex);
	}

	public override bool IsPointInteractionDisabled(int ptIndex)
	{
		if (ptIndex != 0)
		{
			if (_editingMode == EditingMode.Editing)
			{
				return ptIndex == Vector.Count - 1;
			}
			return false;
		}
		return true;
	}

	public bool TryPreviewSnapToNode(HitTestArgs args, SnapMode mode = SnapMode.AppendSnap)
	{
		if (!NetworkRenderLayer.HitTestNode(_network, args, out var elementIndex))
		{
			return false;
		}
		if (!CanFinishAddingAtNode(elementIndex))
		{
			return false;
		}
		PreviewSnapEndToNode(elementIndex, mode);
		return true;
	}

	public void PreviewSnapEndToNode(int nodeIndex, SnapMode mode = SnapMode.AppendSnap)
	{
		Point point = _network.Nodes[nodeIndex].Point;
		string channel = "arc_snap_point";
		_overlay.TryGetVector(channel, out var id);
		VectorManager.AddOrUpdateVector(ref id, point, DefaultEditSymbology.TopologyAddNodeSymbology);
		VectorManager.SetVectorZIndex(id, 9);
		_overlay.RegisterVector(channel, id);
		PreviewSnap(mode, point);
	}

	private void PreviewSnap(SnapMode mode, Point snapPt)
	{
		switch (mode)
		{
		case SnapMode.AppendSnap:
			PreviewAppendPoint(snapPt, removeUnusedPreviewVectors: false);
			break;
		case SnapMode.MoveSnap:
		{
			if (_previewScratchSpaceList.Count == 0 || _previewScratchSpaceList[0] != Count - 1)
			{
				_previewScratchSpaceList.Clear();
				_previewScratchSpaceList.Add(Count - 1);
			}
			List<int> previewScratchSpaceList = _previewScratchSpaceList;
			ArcPolylineWrapper vector = Vector;
			PreviewMovePoints(previewScratchSpaceList, snapPt - vector[vector.Count - 1], isOpenEnded: true, removeUnusedPreviewVectors: false);
			break;
		}
		default:
			throw new NotImplementedException();
		}
	}

	public bool TryPreviewSnapToArc(HitTestArgs args, SnapMode mode = SnapMode.AppendSnap)
	{
		if (!NetworkRenderLayer.HitTestArcs(_network, args, out var _, out var hitArcs))
		{
			return false;
		}
		for (int i = 0; i < hitArcs.Count; i++)
		{
			int fid = hitArcs[i].Fid;
			if (!CanSplitArcAtPoint(args.QueryPoint, fid))
			{
				hitArcs.RemoveAt(i);
				i--;
			}
		}
		SnapResult snapResult = SnapHelpers.TrySnap(args, hitArcs, _network.GetArcPolyline);
		if (!snapResult.Success)
		{
			return false;
		}
		PreviewSnapEndToArc(snapResult.SnapPoint, snapResult.SplitLines, mode);
		return true;
	}

	public void PreviewSnapEndToArc(Point splitPoint, IList<SplitLineResult> splits, SnapMode mode = SnapMode.AppendSnap)
	{
		EditFunctions.RenderSplitsPreview(VectorManager, _overlay, splits, _network.GetArcPolyline, splitPoint, SplitPreviewSymbology.Polyline, "arc_snap");
		PreviewSnap(mode, splitPoint);
	}

	public override bool TryPreviewAttachEndPoint(HitTestArgs hitTest)
	{
		return false;
	}

	public override void PreviewAttachEndPoint(HitTestArgs hitTest)
	{
		throw new NotSupportedException("Attach is not supported for Arcs");
	}

	public override bool TryAttachEndPoint(HitTestArgs hitTest, out int insertedIndex)
	{
		insertedIndex = -1;
		return false;
	}

	public override void AttachEndPoint(HitTestArgs hitTest)
	{
		throw new NotSupportedException("Attach is not supported for Arcs");
	}

	public override bool TryPreviewAppendPoint()
	{
		if (Arc.EndNode != -1)
		{
			return false;
		}
		return base.TryPreviewAppendPoint();
	}

	public override void PreviewAppendPoint(Point point, bool removeUnusedPreviewVectors = true, bool isOpenEnded = true)
	{
		if (Arc.EndNode != -1)
		{
			throw new NotSupportedException("Appending to an arc is not supported unless it is being initialized and does not have two end nodes yet.");
		}
		base.PreviewAppendPoint(point, removeUnusedPreviewVectors, isOpenEnded);
	}

	public override bool TryAppendPoint(HitTestArgs args, bool allowDuplicatePoints = false)
	{
		if (Arc.EndNode != -1)
		{
			return false;
		}
		return base.TryAppendPoint(args, allowDuplicatePoints);
	}

	public override void AppendPoint(HitTestArgs args, bool allowDuplicatePoints = false)
	{
		if (Arc.EndNode != -1)
		{
			throw new NotSupportedException("Appending to an arc is not supported unless it is being initialized and does not have two end nodes yet.");
		}
		base.AppendPoint(args);
	}

	public override bool TryPreviewPrependPoint()
	{
		return false;
	}

	public override void PreviewPrependPoint(HitTestArgs hitTest)
	{
		throw new NotSupportedException("Preprend is not supported for Arcs");
	}

	public override bool TryPrependPoint()
	{
		return false;
	}

	public override void PrependPoint(HitTestArgs hitTest)
	{
		throw new NotSupportedException("Preprend is not supported for Arcs");
	}

	protected override bool ValidateMoveSelectedPoints(HitTestArgs location)
	{
		foreach (int element in SelectedPoints.Elements)
		{
			if (element == 2 && NetworkRenderLayer.HitTestNode(_network, location, out var elementIndex) && elementIndex == Arc.StartNode)
			{
				return false;
			}
			if (element == Vector.Count - 3 && NetworkRenderLayer.HitTestNode(_network, location, out var elementIndex2) && elementIndex2 == Arc.EndNode)
			{
				return false;
			}
		}
		return base.ValidateMoveSelectedPoints(location);
	}
}
