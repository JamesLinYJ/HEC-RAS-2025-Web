using Geospatial.Rendering.Editing.Options;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public static class TopologySelectionOptions
{
	public static readonly OptionKey<TopologySelectionScope> Scope = OptionKey<TopologySelectionScope>.Define("Topology.SelectionScope", "Selection Scope", "Restrict selection to arcs, nodes, regions, or allow all primitives.", TopologySelectionScope.All);
}
