using System.Collections.Generic;
using Geospatial.Rendering.Editing;
using Geospatial.Vectors;
using Geospatial.Vectors.Graph;
using Utility.DataStructures;

namespace Geospatial.Rendering.Layers;

public class GraphRenderLayer : NetworkRenderLayer<Graph, GraphArc>
{
	public GraphRenderLayer(Any<Graph> graph)
		: base(graph)
	{
	}

	private static void ClearScopedIfReplace(SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, SelectMode mode, GraphSelectionScope scope)
	{
		if (mode == SelectMode.Replace)
		{
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

	private static bool InfiniteSelectHelperAll(Graph graph, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, SelectMode mode, GraphSelectionScope scope)
	{
		bool includesArcsAndInfiniteExtentSelectHelper = scope.IncludesArcs() && VectorRenderLayer.InfiniteExtentSelectHelper(arcSelectedSet, mode, graph.Arcs.Count);
		bool includesNodesAndInfiniteExtentSelectHelper = scope.IncludesNodes() && VectorRenderLayer.InfiniteExtentSelectHelper(nodeSelectedSet, mode, graph.Nodes.Count);
		return includesArcsAndInfiniteExtentSelectHelper | includesNodesAndInfiniteExtentSelectHelper;
	}

	public override bool Select(Polyline lineIntersection, SelectMode mode)
	{
		return Select(lineIntersection, mode, GraphSelectionScope.All);
	}

	public bool Select(Polyline lineIntersection, SelectMode mode, GraphSelectionScope scope)
	{
		return Select(_network, ArcSelectedSet, NodeSelectedSet, lineIntersection, mode, scope);
	}

	public static bool Select(Graph graph, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, Polyline lineIntersection, SelectMode mode, GraphSelectionScope scope = GraphSelectionScope.All)
	{
		using (graph.FreezeSafely())
		{
			if (lineIntersection == null || lineIntersection.Count == 0)
			{
				return InfiniteSelectHelperAll(graph, arcSelectedSet, nodeSelectedSet, mode, scope);
			}
			ClearScopedIfReplace(arcSelectedSet, nodeSelectedSet, mode, scope);
			if (scope.IncludesArcs())
			{
				NetworkRenderLayer<Graph, GraphArc>.SelectArcsCrossedBy(graph, arcSelectedSet, lineIntersection, mode);
			}
			return false;
		}
	}

	public override bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		return Select(lasso, hitMode, mode, GraphSelectionScope.All);
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode, GraphSelectionScope scope)
	{
		return Select(_network, ArcSelectedSet, NodeSelectedSet, lasso, hitMode, mode, scope);
	}

	public static bool Select(Graph graph, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, Polygon lasso, MarqueeHitMode hitMode, SelectMode mode, GraphSelectionScope scope = GraphSelectionScope.All)
	{
		using (graph.FreezeSafely())
		{
			if (lasso == null || lasso.Count < 3)
			{
				return InfiniteSelectHelperAll(graph, arcSelectedSet, nodeSelectedSet, mode, scope);
			}
			ClearScopedIfReplace(arcSelectedSet, nodeSelectedSet, mode, scope);
			if (scope.IncludesNodes())
			{
				NetworkRenderLayer<Graph, GraphArc>.SelectNodesInLasso(graph, nodeSelectedSet, lasso, mode);
			}
			if (scope.IncludesArcs())
			{
				NetworkRenderLayer<Graph, GraphArc>.SelectArcsInLasso(graph, arcSelectedSet, lasso, hitMode, mode);
			}
			return false;
		}
	}

	public override bool Select(Extent selectExt, SelectMode mode)
	{
		return Select(selectExt, MarqueeHitMode.Intersects, mode, GraphSelectionScope.All);
	}

	public bool Select(Extent selectExt, MarqueeHitMode hitMode, SelectMode mode, GraphSelectionScope scope)
	{
		return Select(_network, ArcSelectedSet, NodeSelectedSet, selectExt, hitMode, mode, scope);
	}

	public static bool Select(Graph graph, SelectedSet arcSelectedSet, SelectedSet nodeSelectedSet, Extent selectExt, MarqueeHitMode hitMode, SelectMode mode, GraphSelectionScope scope = GraphSelectionScope.All)
	{
		using (graph.FreezeSafely())
		{
			if (selectExt.Boundedness == Extent.State.Invalid)
			{
				return false;
			}
			if (selectExt.Boundedness == Extent.State.Infinite)
			{
				return InfiniteSelectHelperAll(graph, arcSelectedSet, nodeSelectedSet, mode, scope);
			}
			ClearScopedIfReplace(arcSelectedSet, nodeSelectedSet, mode, scope);
			Polygon extentPgon = NetworkRenderLayer<Graph, GraphArc>.FillExtentPolygon(selectExt);
			HashSet<int> intSet = new HashSet<int>();
			HashSet<int> intSetSet = new HashSet<int>();
			if (scope.IncludesNodes())
			{
				NetworkRenderLayer<Graph, GraphArc>.CollectNodeCandidatesInExtent(graph, selectExt, intSet);
			}
			if (scope.IncludesArcs())
			{
				NetworkRenderLayer<Graph, GraphArc>.CollectArcCandidatesInExtent(graph, selectExt, extentPgon, hitMode, intSetSet);
			}
			SelectMode mode2 = mode;
			if (mode == SelectMode.Toggle)
			{
				mode2 = ((nodeSelectedSet.ContainsAll(intSet) && arcSelectedSet.ContainsAll(intSetSet)) ? SelectMode.Remove : SelectMode.Append);
			}
			foreach (int ordinal in intSet)
			{
				NetworkRenderLayer<Graph, GraphArc>.SelectHelper(nodeSelectedSet, ordinal, mode2);
			}
			foreach (int ordinal2 in intSetSet)
			{
				NetworkRenderLayer<Graph, GraphArc>.SelectHelper(arcSelectedSet, ordinal2, mode2);
			}
			return false;
		}
	}
}
