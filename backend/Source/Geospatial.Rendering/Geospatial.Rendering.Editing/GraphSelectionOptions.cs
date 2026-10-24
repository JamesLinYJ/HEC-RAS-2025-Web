using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Editing;

public static class GraphSelectionOptions
{
	public static readonly OptionKey<GraphSelectionScope> Scope = OptionKey<GraphSelectionScope>.Define("Graph.SelectionScope", "Selection Scope", "Restrict selection to arcs, nodes, or allow all primitives.", GraphSelectionScope.All);
}
