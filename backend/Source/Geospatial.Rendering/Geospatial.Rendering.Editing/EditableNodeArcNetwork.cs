using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Rendering;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public abstract class EditableNodeArcNetwork<T> : EditableVector<T>, IEditableNodeArcNetwork where T : BaseTopology
{
	protected readonly string _previewAddArcVectorID;

	private readonly string _previewOpenArcVectorID;

	private readonly string _previewOpenArcSymbolsVectorID;

	private readonly string _previewNodeSegmentConnectionsChannel;

	private readonly string _previewNodeSegmentConnectionsBackgroundChannel;

	private readonly string _allArcsVectorID;

	private readonly string _allNodesVectorID;

	protected readonly string _previewMoveSegmentsVectorID;

	protected readonly string _previewMoveNodeVectorID;

	protected readonly string _splitChannelPrefix;

	private Any<T> _anyNetwork;

	private VectorSymbology _previewNodeOverlay = new VectorSymbology(Color.FromArgb(255, 20, 77, 5), 2.0, Color.FromArgb(0, 91, 204, 59))
	{
		PointSize = 5
	};

	private VectorSymbology _previewNodeUnderlay = new VectorSymbology(Color.FromArgb(255, 20, 77, 5), 2.0, Color.FromArgb(255, 91, 204, 59))
	{
		PointSize = 5
	};

	private VectorSymbology _orangeHighlight = new VectorSymbology(Color.FromArgb(196, 255, 179, 0), 8.0, default);

	public override T Vector => _anyNetwork;

	protected EditableNodeArcNetwork(string channelPrefix, string splitChannelPrefix, Any<T> network, OverlayChannels overlayChannels, IVectorManager vectorManager, Func<EditOperation, bool, bool> executeEditOperation)
		: base((T)null, vectorManager, VectorType.Point, executeEditOperation, overlayChannels)
	{
		_anyNetwork = network;
		_splitChannelPrefix = splitChannelPrefix;
		_previewAddArcVectorID = channelPrefix + "_AddArc";
		_previewMoveSegmentsVectorID = channelPrefix + "_MoveSegments";
		_previewMoveNodeVectorID = channelPrefix + "_MoveNode";
		_previewOpenArcVectorID = channelPrefix + "_OpenArc";
		_previewOpenArcSymbolsVectorID = channelPrefix + "_OpenArcSymbols";
		_previewNodeSegmentConnectionsChannel = channelPrefix + "_NodeSegmentConnections";
		_previewNodeSegmentConnectionsBackgroundChannel = channelPrefix + "_NodeSegmentConnectionsBackground";
		_allArcsVectorID = channelPrefix + "_AllArcs";
		_allNodesVectorID = channelPrefix + "_AllNodes";
	}

	public override bool IsValid()
	{
		return true;
	}

	public void PreviewAddArc(HitTestArgs args)
	{
		_overlay.TryGetVector(_previewAddArcVectorID, out var id);
		VectorManager.AddOrUpdateVector(ref id, args.QueryPoint, DefaultEditSymbology.TopologyAddNodeSymbology);
		VectorManager.SetVectorZIndex(id, 7);
		_overlay.RegisterVector(_previewAddArcVectorID, id);
	}

	public bool TryPreviewSelectArc(HitTestArgs args)
	{
		if (!NetworkRenderLayer.HitTestArc(Vector, args, HitTestSortOrder.Distance, out var _, out var elementIndex, out var _))
		{
			return false;
		}
		PreviewSelectArc(elementIndex);
		PreviewRenderArcDirectionArrows(args.Window, elementIndex);
		return true;
	}

	public void PreviewSelectArc(int arcIndex)
	{
		_overlay.TryGetVector(_previewOpenArcVectorID, out var id);
		VectorManager.AddOrUpdateVector(ref id, Vector.GetArcPolyline(arcIndex), DefaultEditSymbology.SelectLineSymbology);
		VectorManager.SetVectorZIndex(id, 7);
		_overlay.RegisterVector(_previewOpenArcVectorID, id);
	}

	public void PreviewRenderArcDirectionArrows(RasterDefinition window, int arcIdx)
	{
		Polyline arcPolyline = Vector.GetArcPolyline(arcIdx);
		List<Segment> chevronsAlongLine = RepeatedSymbolRenderer.GetChevronsAlongLine(window, arcPolyline, (int _, Polyline pline) => pline, new CompactList<FeatureSegmentSubset>(new FeatureSegmentSubset(0, arcPolyline.SegmentCount - 1)
		{
			Fid = arcIdx
		}));
		_overlay.TryGetVector(_previewOpenArcSymbolsVectorID, out var id);
		VectorManager.AddOrUpdateVector(ref id, chevronsAlongLine, DefaultEditSymbology.SelectLineSymbology);
		_overlay.RegisterVector(_previewOpenArcSymbolsVectorID, id);
	}

	public bool TryPreviewSelectNode(HitTestArgs args)
	{
		if (!NetworkRenderLayer.HitTestNode(Vector, args, out var elementIndex))
		{
			return false;
		}
		PreviewSelectNode(elementIndex);
		return true;
	}

	public void PreviewSelectNode(int nodeIndex)
	{
		Geospatial.Vectors.Point point = Vector.Nodes[nodeIndex].Point;
		_overlay.TryGetVector(_previewAddArcVectorID, out var id);
		_overlay.TryGetVector(_previewNodeSegmentConnectionsChannel, out var id2);
		_overlay.TryGetVector(_previewNodeSegmentConnectionsBackgroundChannel, out var id3);
		VectorManager.AddOrUpdateVector(ref id, point, _previewNodeUnderlay);
		VectorManager.SetVectorZIndex(id, 5);
		VectorManager.AddOrUpdateVector(ref id3, point, _previewNodeOverlay);
		VectorManager.SetVectorZIndex(id3, 41);
		List<Segment> segments = new List<Segment>();
		foreach (NodeArcConnection arc in Vector.Nodes[nodeIndex].Arcs)
		{
			Segment segmentOnArc = (arc.IsStart ? Vector.GetSegmentOnArc(arc.ArcID, 0) : Vector.GetSegmentOnArc(arc.ArcID, Vector.GetArcSegmentCount(arc.ArcID) - 1));
			segments.Add(segmentOnArc);
		}
		VectorManager.AddOrUpdateVector(ref id2, segments, _orangeHighlight);
		VectorManager.SetVectorZIndex(id2, 0);
		_overlay.RegisterVector(_previewAddArcVectorID, id);
		_overlay.RegisterVector(_previewNodeSegmentConnectionsChannel, id2);
		_overlay.RegisterVector(_previewNodeSegmentConnectionsBackgroundChannel, id3);
	}

	public virtual bool TryPreviewMoveSelectedNodes(HitTestArgs hit, Vector delta, IEnumerable<int> nodeIndices)
	{
		PreviewMoveNodes(delta, nodeIndices);
		return true;
	}

	public void PreviewMoveNodes(Vector deltaOffset, IEnumerable<int> nodeIndices)
	{
		if (!nodeIndices.Any())
		{
			return;
		}
		_overlay.TryGetVector(_previewMoveSegmentsVectorID, out var id);
		_overlay.TryGetVector(_previewMoveNodeVectorID, out var id2);
		foreach (int nodeIndex in nodeIndices)
		{
			IReadOnlyList<Segment> nodeAttachedSegments = GetNodeAttachedSegments(nodeIndex);
			EditFunctions.RenderPointWithAttachedSegmentsMoveOperation(VectorManager, ref id, ref id2, nodeAttachedSegments, nodeIndices.Select((int idx) => Vector.Nodes[idx].Point), deltaOffset, DefaultEditSymbology.TopologyEditArcSymbology, DefaultEditSymbology.TopologyEditNodeSymbology);
		}
		_overlay.RegisterVector(_previewMoveSegmentsVectorID, id);
		_overlay.RegisterVector(_previewMoveNodeVectorID, id2);
	}

	protected IReadOnlyList<Segment> GetNodeAttachedSegments(int nodeIndex)
	{
		return NodeArcEditGeometry.GetNodeAttachedSegments(Vector, nodeIndex);
	}

	public bool TryPreviewSplitArcs(HitTestArgs args)
	{
		if (!NetworkRenderLayer.HitTestArcs(Vector, args, out var _, out var hitArcs))
		{
			return false;
		}
		PreviewSplitArcs(args, hitArcs);
		return true;
	}

	public void PreviewSplitArcs(HitTestArgs args, IList<FeatureSegmentSubset> arcIndices)
	{
		SnapResult snapResult = SnapHelpers.TrySnap(args, arcIndices, Vector.GetArcPolyline);
		if (snapResult.Result != SnapMode.NoSnap && snapResult.SplitLines.Count != 0)
		{
			EditFunctions.RenderSplitsPreview(VectorManager, _overlay, snapResult.SplitLines, Vector.GetArcPolyline, snapResult.SnapPoint, SplitPreviewSymbology.Polyline, _splitChannelPrefix);
		}
	}

	public bool TryPreviewSplitArcsAtVertex(HitTestArgs args)
	{
		if (!NetworkRenderLayer.HitTestArcs(Vector, args, out var _, out var hitArcs))
		{
			return false;
		}
		if (SnapHelpers.TrySnapToVertexOnly(args, hitArcs, Vector.GetArcPolyline, out var splitRes) == SnapMode.NoSnap)
		{
			return false;
		}
		if (splitRes.SnappedVertices.FirstValue == 0)
		{
			return false;
		}
		Polyline arcPolyline = Vector.GetArcPolyline(splitRes.FID);
		if (splitRes.SnappedVertices.FirstValue == arcPolyline.Count - 1)
		{
			return false;
		}
		EditFunctions.RenderSplitPreview(VectorManager, _overlay, splitRes, Vector.GetArcPolyline, SplitPreviewSymbology.Polyline, _splitChannelPrefix);
		return true;
	}

	public void PreviewAllArcs(List<Polyline> scratchSpace = null, IList<int> blacklist = null)
	{
		if (scratchSpace == null)
		{
			scratchSpace = new List<Polyline>();
		}
		using (Vector.FreezeSafely())
		{
			scratchSpace.Clear();
			for (int i = 0; i < Vector.ArcCount; i++)
			{
				if (blacklist == null || !blacklist.Contains(i))
				{
					scratchSpace.Add(Vector.GetArcPolyline(i));
				}
			}
			_overlay.TryGetVector(_allArcsVectorID, out var id);
			VectorManager.AddOrUpdateVector(ref id, scratchSpace, DefaultEditSymbology.TopologyArcSymbology);
			_overlay.RegisterVector(_allArcsVectorID, id);
		}
	}

	public void PreviewAllNodes(List<Geospatial.Vectors.Point> scratchSpace = null, IList<int> blacklist = null)
	{
		if (scratchSpace == null)
		{
			scratchSpace = new List<Geospatial.Vectors.Point>();
		}
		using (Vector.FreezeSafely())
		{
			scratchSpace.Clear();
			for (int i = 0; i < Vector.NodeCount; i++)
			{
				if (blacklist == null || !blacklist.Contains(i))
				{
					scratchSpace.Add(Vector.Nodes[i].Point);
				}
			}
			_overlay.TryGetVector(_allNodesVectorID, out var id);
			VectorManager.AddOrUpdateVector(ref id, scratchSpace, DefaultEditSymbology.TopologyNodeSymbology);
			_overlay.RegisterVector(_allNodesVectorID, id);
		}
	}

	public void SetPreviewAllSymbology(VectorSymbology arcSymbology, VectorSymbology nodeSymbology)
	{
		VectorID vector = _overlay.GetVector(_allArcsVectorID);
		if (!vector.IsNull())
		{
			VectorManager.TryUpdateSymbology(vector, arcSymbology);
		}
		VectorID vector2 = _overlay.GetVector(_allNodesVectorID);
		if (!vector2.IsNull())
		{
			VectorManager.TryUpdateSymbology(vector2, nodeSymbology);
			VectorManager.SetVectorZIndex(vector2, 6);
		}
	}
}
