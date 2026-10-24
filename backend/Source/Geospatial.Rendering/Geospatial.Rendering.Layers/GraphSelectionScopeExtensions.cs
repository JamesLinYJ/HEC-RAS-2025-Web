using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public static class GraphSelectionScopeExtensions
{
	public static bool IncludesArcs(this GraphSelectionScope s)
	{
		if (s != GraphSelectionScope.All)
		{
			return s == GraphSelectionScope.Arcs;
		}
		return true;
	}

	public static bool IncludesNodes(this GraphSelectionScope s)
	{
		if (s != GraphSelectionScope.All)
		{
			return s == GraphSelectionScope.Nodes;
		}
		return true;
	}

	public static bool Includes(this GraphSelectionScope s, NetworkElementType element)
	{
		switch (element)
		{
		case NetworkElementType.Node:
			return s.IncludesNodes();
		case NetworkElementType.ArcSegment:
		case NetworkElementType.ArcInternalPoint:
			return s.IncludesArcs();
		default:
			return true;
		}
	}
}
