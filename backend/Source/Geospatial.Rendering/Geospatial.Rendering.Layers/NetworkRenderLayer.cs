using System;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Helpers;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Rendering;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Interfaces;
using Utility.Logging;

namespace Geospatial.Rendering.Layers;

public abstract class NetworkRenderLayer : RenderLayer
{
	public const int NodePixelHitDistance = 6;

	public const int ArcInternalPointPixelHitDistance = 5;

	public const int ArcSegmentPixelHitDistance = 5;

	public SelectedSet NodeSelectedSet = new SelectedSet();

	public SelectedSet ArcSelectedSet = new SelectedSet();

	public virtual long GetSelectionVersion()
	{
		return ArcSelectedSet.VersionXY + NodeSelectedSet.VersionXY;
	}

	public static bool HitTest(BaseTopology network, HitTestArgs args, out NetworkElementType element, out int elementIndex, out int subElementIndex)
	{
		double cellSize = args.Window.CellSize;
		return network.HitTest(args.QueryPoint, 6.0 * cellSize, 5.0 * cellSize, 5.0 * cellSize, out element, out elementIndex, out subElementIndex);
	}

	public static bool HitTestNode(BaseTopology network, HitTestArgs args, out int elementIndex)
	{
		return network.HitTestNode(args.QueryPoint, 6.0 * args.Window.CellSize, out elementIndex);
	}

	public static bool HitTestArc(BaseTopology network, HitTestArgs args, HitTestSortOrder mode, out NetworkElementType element, out int elementIndex, out int subElementIndex)
	{
		return network.HitTestArc(args.QueryPoint, mode, 5.0 * args.Window.CellSize, 5.0 * args.Window.CellSize, out element, out elementIndex, out subElementIndex);
	}

	public static bool HitTestArcs(BaseTopology network, HitTestArgs args, out NetworkElementType element, out IList<FeatureSegmentSubset> hitArcs)
	{
		return network.HitTestArcs(args.QueryPoint, 5.0 * args.Window.CellSize, 5.0 * args.Window.CellSize, out element, out hitArcs);
	}
}
public abstract class NetworkRenderLayer<TNetwork, TArc> : NetworkRenderLayer, IVectorRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IBasicSelection, ISelection where TNetwork : BaseTopology<TArc> where TArc : BaseArc, IEquatable<TArc>, new()
{
	private static readonly ILogger _logger = LogHelper.GetLogger("NetworkRenderLayer");

	private bool _showArcIndexing;

	private bool _showDirectionArrows;

	private bool _showArcVertices;

	protected readonly Any<TNetwork> _network;

	private Versioned<PointCollection> _allInternalPoints = new Versioned<PointCollection>();

	private static readonly ThreadLocal<Polygon> _selectionExtentPolygonCache = new ThreadLocal<Polygon>(() => Polygon.FromExtent(new Extent(0.0, 0.0, 1.0, 1.0)));

	[PlotOption("Show Arc Indices")]
	public bool ShowArcIndexing
	{
		get
		{
			return _showArcIndexing;
		}
		set
		{
			if (_showArcIndexing != value)
			{
				_showArcIndexing = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Arc Direction Arrows")]
	public bool ShowDirectionArrows
	{
		get
		{
			return _showDirectionArrows;
		}
		set
		{
			if (_showDirectionArrows != value)
			{
				_showDirectionArrows = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Arc Vertices")]
	public bool ShowArcVertices
	{
		get
		{
			return _showArcVertices;
		}
		set
		{
			if (_showArcVertices != value)
			{
				_showArcVertices = value;
				IncrementRenderState();
			}
		}
	}

	private PointCollection AllInternalPoints => _allInternalPoints.Get(_network.Value.VersionXY, () =>
	{
		TNetwork network = _network;
		int ordinal = 0;
		foreach (TArc arc in network.Arcs)
		{
			ordinal += arc.InternalPoints?.Count ?? 0;
		}
		PointCollection pointCollection = new PointCollection(ordinal);
		foreach (TArc arc2 in network.Arcs)
		{
			if (arc2.InternalPoints != null)
			{
				pointCollection.AddRange(arc2.InternalPoints);
			}
		}
		return pointCollection;
	});

	public override Extent ValidExtent => _network.Value.Extent;

	public VectorSymbology VectorSymbology { get; set; } = DefaultEditSymbology.TopologyArcSymbology;

	public VectorSymbology NodeSymbology { get; set; } = DefaultEditSymbology.TopologyNodeSymbology;

	public VectorSymbology InternalPointSymbology { get; set; } = DefaultEditSymbology.TopologyArcInternalPointSymbology;

	public int Count => 1;

	protected virtual bool AnySelected
	{
		get
		{
			if (ArcSelectedSet.Count <= 0)
			{
				return NodeSelectedSet.Count > 0;
			}
			return true;
		}
	}

	SelectedSet IVectorRenderLayer.SelectedSet => new SelectedSet();

	protected NetworkRenderLayer(Any<TNetwork> network)
	{
		_network = network;
	}

	public ISpatialVersion GetSpatialVersionContext()
	{
		return _network.Value;
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		TNetwork network = _network;
		if (!network.Lock.TryEnterReadLock(10))
		{
			_logger.LogDebounce(LogLevel.Warning, "Failed to acquire " + typeof(TNetwork).Name + " read lock.");
			return false;
		}
		try
		{
			if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
			{
				return false;
			}
			return RenderInternalHelper(network, plot, ref cache);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to render {Network}.", typeof(TNetwork).Name);
			return false;
		}
		finally
		{
			network.Lock.ExitReadLock();
		}
	}

	protected virtual bool RenderInternalHelper(TNetwork network, PlotRequest request, ref object cache)
	{
		if (!ValidExtent.Overlaps(request.RasterQuery.Extent))
		{
			return false;
		}
		using (network.FreezeSafely())
		{
			bool condition = false;
			List<FeatureSegmentSubset> elements = network.ArcSpatialIndex.GetElements(request.RasterQuery.Extent, dedupe: false);
			elements.Dedupe();
			if (ShowDirectionArrows)
			{
				RepeatedSymbolRenderer.RenderArrowsAlongLine(request, network, (int idx, TNetwork mdl) => mdl.GetArcPolyline(idx), elements, VectorSymbology, 64.0, 8.0);
			}
			if (ShowArcIndexing)
			{
				foreach (FeatureSegmentSubset element in elements)
				{
					Point arcMidpoint = network.GetArcMidpoint(element.Fid);
					request.AddLabel(new LazyLabelDefinition<int>((int idx) => "Arc " + idx, element.Fid, arcMidpoint));
				}
			}
			condition |= PolylineRenderLayer.Renderer.Render<Polyline, TNetwork>(request, network, (int idx, TNetwork mdl) => mdl.GetArcPolyline(idx), elements, VectorSymbology);
			if (ShowArcVertices)
			{
				condition |= PointRenderLayer.PaintUniformSymbology(AllInternalPoints, InternalPointSymbology, request);
			}
			List<int> elements2 = network.NodeSpatialIndex.GetElements(request.RasterQuery.Extent);
			return condition | PaintNodes(network, elements2, request);
		}
	}

	protected virtual bool PaintNodes(TNetwork network, List<int> nodeCandidateIndices, PlotRequest request)
	{
		return PointRenderLayer.PaintPoints(nodeCandidateIndices, (int idx) => network.Nodes[idx].Point, NodeSymbology, request);
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _network.Value.VersionXY + VectorSymbology.VersionXY + NodeSymbology.VersionXY + InternalPointSymbology.VersionXY;
	}

	protected static void SelectHelper(SelectedSet selectionSet, int elementIndex, SelectMode mode)
	{
		switch (mode)
		{
		case SelectMode.Append:
		case SelectMode.Replace:
			selectionSet.Add(elementIndex);
			break;
		case SelectMode.Remove:
			selectionSet.Remove(elementIndex);
			break;
		case SelectMode.Toggle:
			if (selectionSet.Contains(elementIndex))
			{
				selectionSet.Remove(elementIndex);
			}
			else
			{
				selectionSet.Add(elementIndex);
			}
			break;
		case SelectMode.EnsureIncluded:
			if (!selectionSet.Contains(elementIndex))
			{
				selectionSet.Clear();
				selectionSet.Add(elementIndex);
			}
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		TNetwork network = _network;
		using (network.FreezeSafely())
		{
			if (mode == SelectMode.Replace)
			{
				ClearSelectedSets();
			}
			if (mode == SelectMode.EnsureIncluded && AnySelected)
			{
				return true;
			}
			if (!NetworkRenderLayer.HitTest(network, selectArgs, out var element, out var elementIndex, out var _))
			{
				return false;
			}
			return SelectHitElement(network, element, elementIndex, mode);
		}
	}

	protected virtual void ClearSelectedSets()
	{
		ArcSelectedSet.Clear();
		NodeSelectedSet.Clear();
	}

	protected virtual bool SelectHitElement(TNetwork network, NetworkElementType element, int elementIndex, SelectMode mode)
	{
		switch (element)
		{
		case NetworkElementType.Node:
			SelectHelper(NodeSelectedSet, elementIndex, mode);
			return true;
		case NetworkElementType.ArcSegment:
		case NetworkElementType.ArcInternalPoint:
			SelectHelper(ArcSelectedSet, elementIndex, mode);
			return true;
		default:
			throw new NotImplementedException();
		}
	}

	public abstract bool Select(Extent selectExt, SelectMode mode);

	public abstract bool Select(Polyline lineIntersection, SelectMode mode);

	public abstract bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode);

	protected static void SelectArcsCrossedBy(TNetwork network, SelectedSet arcSelectedSet, Polyline lineIntersection, SelectMode mode)
	{
		foreach (FeatureSegmentSubset element in network.ArcSpatialIndex.GetElements(lineIntersection.Extent))
		{
			Polyline arcPolyline = network.GetArcPolyline(element.Fid);
			if (lineIntersection.Intersects(arcPolyline))
			{
				SelectHelper(arcSelectedSet, element.Fid, mode);
			}
		}
	}

	protected static void SelectNodesInLasso(TNetwork network, SelectedSet nodeSelectedSet, Polygon lasso, SelectMode mode)
	{
		foreach (int element in network.NodeSpatialIndex.GetElements(lasso.Extent))
		{
			if (lasso.Contains(network.Nodes[element].Point))
			{
				SelectHelper(nodeSelectedSet, element, mode);
			}
		}
	}

	protected static void SelectArcsInLasso(TNetwork network, SelectedSet arcSelectedSet, Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		foreach (FeatureSegmentSubset element in network.ArcSpatialIndex.GetElements(lasso.Extent))
		{
			Polyline arcPolyline = network.GetArcPolyline(element.Fid);
			if ((hitMode == MarqueeHitMode.Contained) ? lasso.Contains(arcPolyline) : lasso.ContainsOrIntersects(arcPolyline))
			{
				SelectHelper(arcSelectedSet, element.Fid, mode);
			}
		}
	}

	protected static Polygon FillExtentPolygon(Extent selectExt)
	{
		Polygon polygon = _selectionExtentPolygonCache.Value;
		polygon[0] = new Point(selectExt.MinX, selectExt.MinY);
		polygon[1] = new Point(selectExt.MinX, selectExt.MaxY);
		polygon[2] = new Point(selectExt.MaxX, selectExt.MaxY);
		polygon[3] = new Point(selectExt.MaxX, selectExt.MinY);
		return polygon;
	}

	protected static void CollectNodeCandidatesInExtent(TNetwork network, Extent selectExt, HashSet<int> candidateNodes)
	{
		network.NodeSpatialIndex.GetBinRange(selectExt, out var completelyCoveredBins, out var partiallyCoveredBins);
		foreach (int completelyCoveredBin in completelyCoveredBins)
		{
			foreach (int bin in network.NodeSpatialIndex.Bins[completelyCoveredBin])
			{
				candidateNodes.Add(bin);
			}
		}
		foreach (int partiallyCoveredBin in partiallyCoveredBins)
		{
			foreach (int bin2 in network.NodeSpatialIndex.Bins[partiallyCoveredBin])
			{
				if (selectExt.ContainsOrTouches(network.Nodes[bin2].Point))
				{
					candidateNodes.Add(bin2);
				}
			}
		}
	}

	protected static void CollectArcCandidatesInExtent(TNetwork network, Extent selectExt, Polygon extentPgon, MarqueeHitMode hitMode, HashSet<int> candidateArcs)
	{
		if (hitMode == MarqueeHitMode.Contained)
		{
			foreach (FeatureSegmentSubset element in network.ArcSpatialIndex.GetElements(selectExt))
			{
				Polyline arcPolyline = network.GetArcPolyline(element.Fid);
				if (extentPgon.Contains(arcPolyline))
				{
					candidateArcs.Add(element.Fid);
				}
			}
			return;
		}
		network.ArcSpatialIndex.GetBinRange(selectExt, out var completelyCoveredBins, out var partiallyCoveredBins);
		foreach (int completelyCoveredBin in completelyCoveredBins)
		{
			foreach (FeatureSegmentSubset bin in network.ArcSpatialIndex.Bins[completelyCoveredBin])
			{
				candidateArcs.Add(bin.Fid);
			}
		}
		foreach (int partiallyCoveredBin in partiallyCoveredBins)
		{
			foreach (FeatureSegmentSubset bin2 in network.ArcSpatialIndex.Bins[partiallyCoveredBin])
			{
				Polyline arcPolyline2 = network.GetArcPolyline(bin2.Fid);
				if (extentPgon.ContainsOrIntersects(arcPolyline2))
				{
					candidateArcs.Add(bin2.Fid);
				}
			}
		}
	}

	public virtual IList<object> GetHitTestVectors(HitTestArgs args, out int hitTestedIdx)
	{
		TNetwork network = _network;
		hitTestedIdx = -1;
		if (!NetworkRenderLayer.HitTest(network, args, out var element, out var elementIndex, out var subElementIndex))
		{
			return null;
		}
		hitTestedIdx = HashCode.Combine((int)element, elementIndex, subElementIndex);
		switch (element)
		{
		case NetworkElementType.Node:
			return new CompactList<object>(network.Nodes[elementIndex].Point);
		case NetworkElementType.ArcSegment:
		case NetworkElementType.ArcInternalPoint:
			return new CompactList<object>(network.GetArcPolyline(elementIndex));
		default:
			throw new NotImplementedException();
		}
	}

	IList<object> IBasicSelection.GetSelectedVectors()
	{
		return ((IBasicSelection)this).GetSelectedVectors();
	}

	IReadOnlyList<Point> ISelection.GetSelectedPoints()
	{
		if (NodeSelectedSet.Count == 0)
		{
			return null;
		}
		TNetwork network = _network;
		try
		{
			network.FreezeOne();
			List<Point> points = new List<Point>(NodeSelectedSet.Count);
			foreach (int element in NodeSelectedSet.Elements)
			{
				if (element < network.Nodes.Count)
				{
					points.Add(network.Nodes[element].Point);
				}
			}
			return points;
		}
		finally
		{
			network.ThawOne();
		}
	}

	IReadOnlyList<Polyline> ISelection.GetSelectedPolylines()
	{
		if (ArcSelectedSet.Count == 0)
		{
			return null;
		}
		TNetwork network = _network;
		try
		{
			network.FreezeOne();
			List<Polyline> polylines = new List<Polyline>(ArcSelectedSet.Count);
			foreach (int element in ArcSelectedSet.Elements)
			{
				if (element < network.Arcs.Count)
				{
					polylines.Add(network.GetArcPolyline(element));
				}
			}
			return polylines;
		}
		finally
		{
			network.ThawOne();
		}
	}

	IReadOnlyList<ComplexPolygon> ISelection.GetSelectedComplexPolygons()
	{
		return GetSelectedComplexPolygonsCore();
	}

	protected virtual IReadOnlyList<ComplexPolygon> GetSelectedComplexPolygonsCore()
	{
		return null;
	}
}
