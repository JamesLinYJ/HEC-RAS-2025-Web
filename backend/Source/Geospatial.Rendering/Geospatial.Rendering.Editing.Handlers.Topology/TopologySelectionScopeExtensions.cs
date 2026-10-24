using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public static class TopologySelectionScopeExtensions
{
	public static bool IncludesArcs(this TopologySelectionScope s)
	{
		if (s != TopologySelectionScope.All)
		{
			return s == TopologySelectionScope.Arcs;
		}
		return true;
	}

	public static bool IncludesNodes(this TopologySelectionScope s)
	{
		if (s != TopologySelectionScope.All)
		{
			return s == TopologySelectionScope.Nodes;
		}
		return true;
	}

	public static bool IncludesRegions(this TopologySelectionScope s)
	{
		if (s != TopologySelectionScope.All)
		{
			return s == TopologySelectionScope.Regions;
		}
		return true;
	}

	public static bool Includes(this TopologySelectionScope s, NetworkElementType element)
	{
		switch (element)
		{
		case NetworkElementType.Node:
			return s.IncludesNodes();
		case NetworkElementType.ArcSegment:
		case NetworkElementType.ArcInternalPoint:
			return s.IncludesArcs();
		case NetworkElementType.ControlPoint:
		case NetworkElementType.Region:
			return s.IncludesRegions();
		default:
			return true;
		}
	}
}
