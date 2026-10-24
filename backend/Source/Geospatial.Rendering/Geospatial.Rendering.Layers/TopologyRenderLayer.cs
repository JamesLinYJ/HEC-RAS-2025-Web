using System;
using System.Collections.Generic;
using System.Drawing;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Editing.Handlers.Topology;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Layers;

public class TopologyRenderLayer : NetworkRenderLayer<Topology, Arc>, ILineSourceLayer
{
	public struct TopologyState
	{
		public PlotRequest Req;

		public Topology Topo;

		public VectorSymbology Symb;
	}

	public SelectedSet ControlPointSelectedSet = new SelectedSet();

	public bool IsEnabled = true;

	private Topology _lineSourceFor;

	private TopologyArcLineSource _lineSource;

	public VectorSymbology ControlPointSymbology { get; set; } = new VectorSymbology(Color.Black, 3.0, Color.Red)
	{
		PointSize = 12
	};

	public VectorSymbology DeletableNodeSymbology { get; set; } = DefaultEditSymbology.TopologyDeletableNodeSymbology;

	protected override bool AnySelected
	{
		get
		{
			if (!base.AnySelected)
			{
				return ControlPointSelectedSet.Count > 0;
			}
			return true;
		}
	}

	public bool TryGetLineSource(out ILineSegmentSource source)
	{
		Topology topology = _network;
		if (_lineSource == null || _lineSourceFor != topology)
		{
			_lineSourceFor = topology;
			_lineSource = new TopologyArcLineSource(topology);
		}
		source = _lineSource;
		return true;
	}

	public TopologyRenderLayer(Any<Topology> topo)
		: base(topo)
	{
	}

	protected override bool PaintNodes(Topology topo, List<int> nodeCandidateIndices, PlotRequest request)
	{
		return PointRenderLayer.PaintPoints(new TopologyState
		{
			Req = request,
			Topo = topo,
			Symb = DeletableNodeSymbology
		}, nodeCandidateIndices, (int idx) => topo.Nodes[idx].Point, NodeSymbology, request);
	}

	public static int? ComputeNodeSize(TopologyState state, int nodeIdx)
	{
		double computed = 50.0;
		double computed2 = 2.0;
		Node node = state.Topo.Nodes[nodeIdx];
		double minValue = double.MinValue;
		foreach (NodeArcConnection arc in node.Arcs)
		{
			int arcID = arc.ArcID;
			Polyline arcPolyline = state.Topo.GetArcPolyline(arcID);
			if (arcPolyline.Length > minValue)
			{
				minValue = arcPolyline.Length;
			}
		}
		double minValueOverCellSize = minValue / state.Req.RasterQuery.CellSize;
		if (minValueOverCellSize > computed)
		{
			return null;
		}
		if (minValueOverCellSize < computed2)
		{
			return 0;
		}
		return (int)((double)DefaultEditSymbology.TopologyNodeSymbology.PointSize * (minValueOverCellSize / computed));
	}

	public static Color? GetNodeColor(TopologyState state, int nodeIdx)
	{
		Node node = state.Topo.Nodes[nodeIdx];
		if (node.Arcs.Count == 2 && node.Arcs[0].ArcID != node.Arcs[1].ArcID)
		{
			return state.Symb.FillColor;
		}
		return null;
	}

	public override long GetSelectionVersion()
	{
		return base.GetSelectionVersion() + ControlPointSelectedSet.VersionXY;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + DeletableNodeSymbology.VersionXY;
	}

	private static void ClearScopedIfReplace(SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, SelectedSet controlPointSelectedSet, SelectMode mode, TopologySelectionScope scope)
	{
		if (mode == SelectMode.Replace)
		{
			if (scope.IncludesRegions())
			{
				controlPointSelectedSet.Clear();
			}
			if (scope.IncludesArcs())
			{
				arcSelectedSet.Clear();
			}
			if (scope.IncludesNodes())
			{
				nodeSelectedSet.Clear();
			}
		}
	}

	private static void ForEachHitRegionCtrlPt(Topology topo, Extent ext, Func<Polygon, bool> hit, Action<int> act)
	{
		foreach (int element in topo.RegionSpatialIndex.GetElements(ext))
		{
			Polygon regionOuterRingPolygon = topo.GetRegionOuterRingPolygon(element);
			if (!hit(regionOuterRingPolygon))
			{
				continue;
			}
			foreach (int controlPointIndex in topo.CompletedRegions[element].ControlPointIndices)
			{
				act(controlPointIndex);
			}
		}
	}

	private static bool InfiniteSelectHelperAll(Topology topo, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, SelectedSet controlPointSelectedSet, SelectMode mode, TopologySelectionScope scope = TopologySelectionScope.All)
	{
		bool includesArcsAndInfiniteExtentSelectHelper = scope.IncludesArcs() && VectorRenderLayer.InfiniteExtentSelectHelper(arcSelectedSet, mode, topo.Arcs.Count);
		bool includesNodesAndInfiniteExtentSelectHelper = scope.IncludesNodes() && VectorRenderLayer.InfiniteExtentSelectHelper(nodeSelectedSet, mode, topo.Nodes.Count);
		bool includesRegionsAndInfiniteExtentSelectHelper = scope.IncludesRegions() && VectorRenderLayer.InfiniteExtentSelectHelper(controlPointSelectedSet, mode, topo.RegionControlPoints.Count);
		return includesArcsAndInfiniteExtentSelectHelper | includesNodesAndInfiniteExtentSelectHelper | includesRegionsAndInfiniteExtentSelectHelper;
	}

	public override bool Select(Polyline lineIntersection, SelectMode mode)
	{
		return Select(lineIntersection, mode, TopologySelectionScope.All);
	}

	public bool Select(Polyline lineIntersection, SelectMode mode, TopologySelectionScope scope)
	{
		return Select(_network, ArcSelectedSet, NodeSelectedSet, ControlPointSelectedSet, lineIntersection, mode, scope);
	}

	public static bool Select(Topology topo, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, SelectedSet controlPointSelectedSet, Polyline lineIntersection, SelectMode mode, TopologySelectionScope scope = TopologySelectionScope.All)
	{
		using (topo.FreezeSafely())
		{
			if (lineIntersection == null || lineIntersection.Count == 0)
			{
				return InfiniteSelectHelperAll(topo, arcSelectedSet, nodeSelectedSet, controlPointSelectedSet, mode, scope);
			}
			ClearScopedIfReplace(arcSelectedSet, nodeSelectedSet, controlPointSelectedSet, mode, scope);
			if (scope.IncludesArcs())
			{
				NetworkRenderLayer<Topology, Arc>.SelectArcsCrossedBy(topo, arcSelectedSet, lineIntersection, mode);
			}
			if (scope.IncludesRegions())
			{
				ForEachHitRegionCtrlPt(topo, lineIntersection.Extent, (Polygon regionOuter) => regionOuter.ContainsOrIntersects(lineIntersection), (int ctrlPtIdx) =>
				{
					NetworkRenderLayer<Topology, Arc>.SelectHelper(controlPointSelectedSet, ctrlPtIdx, mode);
				});
			}
			return false;
		}
	}

	public override bool Select(Extent selectExt, SelectMode mode)
	{
		return Select(selectExt, MarqueeHitMode.Intersects, mode, TopologySelectionScope.All);
	}

	public bool Select(Extent selectExt, MarqueeHitMode hitMode, SelectMode mode, TopologySelectionScope scope)
	{
		return Select(_network, ArcSelectedSet, NodeSelectedSet, ControlPointSelectedSet, selectExt, hitMode, mode, scope);
	}

	public override bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		return Select(lasso, hitMode, mode, TopologySelectionScope.All);
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode, TopologySelectionScope scope)
	{
		return Select(_network, ArcSelectedSet, NodeSelectedSet, ControlPointSelectedSet, lasso, hitMode, mode, scope);
	}

	public static bool Select(Topology topo, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, SelectedSet controlPointSelectedSet, Polygon lasso, MarqueeHitMode hitMode, SelectMode mode, TopologySelectionScope scope = TopologySelectionScope.All)
	{
		using (topo.FreezeSafely())
		{
			if (lasso == null || lasso.Count < 3)
			{
				return InfiniteSelectHelperAll(topo, arcSelectedSet, nodeSelectedSet, controlPointSelectedSet, mode, scope);
			}
			ClearScopedIfReplace(arcSelectedSet, nodeSelectedSet, controlPointSelectedSet, mode, scope);
			if (scope.IncludesNodes())
			{
				NetworkRenderLayer<Topology, Arc>.SelectNodesInLasso(topo, nodeSelectedSet, lasso, mode);
			}
			if (scope.IncludesArcs())
			{
				NetworkRenderLayer<Topology, Arc>.SelectArcsInLasso(topo, arcSelectedSet, lasso, hitMode, mode);
			}
			if (scope.IncludesRegions())
			{
				ForEachHitRegionCtrlPt(topo, lasso.Extent, (Polygon regionOuter) => (hitMode != MarqueeHitMode.Contained) ? lasso.AreaIntersects(regionOuter) : lasso.Contains(regionOuter), (int ctrlPtIdx) =>
				{
					NetworkRenderLayer<Topology, Arc>.SelectHelper(controlPointSelectedSet, ctrlPtIdx, mode);
				});
			}
			return false;
		}
	}

	public static bool Select(Topology topo, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, SelectedSet controlPointSelectedSet, Extent selectExt, MarqueeHitMode hitMode, SelectMode mode, TopologySelectionScope scope = TopologySelectionScope.All)
	{
		using (topo.FreezeSafely())
		{
			if (selectExt.Boundedness == Extent.State.Invalid)
			{
				return false;
			}
			if (selectExt.Boundedness == Extent.State.Infinite)
			{
				return InfiniteSelectHelperAll(topo, arcSelectedSet, nodeSelectedSet, controlPointSelectedSet, mode, scope);
			}
			ClearScopedIfReplace(arcSelectedSet, nodeSelectedSet, controlPointSelectedSet, mode, scope);
			Polygon extentPgon = NetworkRenderLayer<Topology, Arc>.FillExtentPolygon(selectExt);
			HashSet<int> intSet = new HashSet<int>();
			HashSet<int> intSetSet = new HashSet<int>();
			HashSet<int> candidateCtrlPts = new HashSet<int>();
			if (scope.IncludesNodes())
			{
				NetworkRenderLayer<Topology, Arc>.CollectNodeCandidatesInExtent(topo, selectExt, intSet);
			}
			if (scope.IncludesArcs())
			{
				NetworkRenderLayer<Topology, Arc>.CollectArcCandidatesInExtent(topo, selectExt, extentPgon, hitMode, intSetSet);
			}
			if (scope.IncludesRegions())
			{
				ForEachHitRegionCtrlPt(topo, selectExt, (Polygon regionOuter) => (hitMode != MarqueeHitMode.Contained) ? extentPgon.ContainsOrIntersects(regionOuter) : extentPgon.Contains(regionOuter), (int ctrlPtIdx) =>
				{
					candidateCtrlPts.Add(ctrlPtIdx);
				});
			}
			SelectMode mode2 = mode;
			if (mode == SelectMode.Toggle)
			{
				mode2 = ((nodeSelectedSet.ContainsAll(intSet) && arcSelectedSet.ContainsAll(intSetSet) && controlPointSelectedSet.ContainsAll(candidateCtrlPts)) ? SelectMode.Remove : SelectMode.Append);
			}
			foreach (int ordinal in intSet)
			{
				NetworkRenderLayer<Topology, Arc>.SelectHelper(nodeSelectedSet, ordinal, mode2);
			}
			foreach (int ordinal2 in intSetSet)
			{
				NetworkRenderLayer<Topology, Arc>.SelectHelper(arcSelectedSet, ordinal2, mode2);
			}
			foreach (int candidateCtrlPt in candidateCtrlPts)
			{
				NetworkRenderLayer<Topology, Arc>.SelectHelper(controlPointSelectedSet, candidateCtrlPt, mode2);
			}
			return false;
		}
	}

	protected override void ClearSelectedSets()
	{
		base.ClearSelectedSets();
		ControlPointSelectedSet.Clear();
	}

	protected override bool SelectHitElement(Topology topo, NetworkElementType element, int elementIndex, SelectMode mode)
	{
		switch (element)
		{
		case NetworkElementType.ControlPoint:
			NetworkRenderLayer<Topology, Arc>.SelectHelper(ControlPointSelectedSet, elementIndex, mode);
			return true;
		case NetworkElementType.Region:
		{
			Geospatial.Vectors.Topology.Region region = topo.CompletedRegions[elementIndex];
			if (region.ControlPointIndices.Count == 1)
			{
				NetworkRenderLayer<Topology, Arc>.SelectHelper(ControlPointSelectedSet, region.ControlPointIndex, mode);
			}
			return true;
		}
		default:
			return base.SelectHitElement(topo, element, elementIndex, mode);
		}
	}

	public override IList<object> GetHitTestVectors(HitTestArgs args, out int hitTestedIdx)
	{
		Topology topology = _network;
		hitTestedIdx = -1;
		if (!NetworkRenderLayer.HitTest(topology, args, out var element, out var elementIndex, out var subElementIndex))
		{
			return null;
		}
		hitTestedIdx = HashCode.Combine((int)element, elementIndex, subElementIndex);
		switch (element)
		{
		case NetworkElementType.ControlPoint:
		{
			if (topology.TryGetControlPointContainingRegion(elementIndex, out var regionIndex))
			{
				if (topology.CompletedRegions[regionIndex].ControlPointIndices.Count > 1)
				{
					return null;
				}
				return new CompactList<object>(topology.GetRegionComplexPolygon(regionIndex));
			}
			return new CompactList<object>(topology.RegionControlPoints[elementIndex]);
		}
		case NetworkElementType.Node:
			return new CompactList<object>(topology.Nodes[elementIndex].Point);
		case NetworkElementType.ArcSegment:
		case NetworkElementType.ArcInternalPoint:
			return new CompactList<object>(topology.GetArcPolyline(elementIndex));
		case NetworkElementType.Region:
			if (topology.CompletedRegions[elementIndex].ControlPointIndices.Count > 1)
			{
				return null;
			}
			return new CompactList<object>(topology.GetRegionComplexPolygon(elementIndex));
		default:
			throw new NotImplementedException();
		}
	}

	protected override IReadOnlyList<ComplexPolygon> GetSelectedComplexPolygonsCore()
	{
		if (ControlPointSelectedSet.Count == 0)
		{
			return null;
		}
		Topology topology = _network;
		try
		{
			topology.FreezeOne();
			List<ComplexPolygon> complexPolygons = new List<ComplexPolygon>(ControlPointSelectedSet.Count);
			foreach (int element in ControlPointSelectedSet.Elements)
			{
				if (element < topology.RegionControlPoints.Count && topology.TryGetControlPointContainingRegion(element, out var regionIndex) && topology.CompletedRegions[regionIndex].ControlPointIndex == element)
				{
					complexPolygons.Add(topology.GetRegionComplexPolygon(regionIndex));
				}
			}
			return complexPolygons;
		}
		finally
		{
			topology.ThawOne();
		}
	}
}
