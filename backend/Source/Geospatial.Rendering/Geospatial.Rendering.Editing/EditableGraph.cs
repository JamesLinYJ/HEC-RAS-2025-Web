using System;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors.Graph;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class EditableGraph : EditableNodeArcNetwork<Graph>
{
	public const string SplitChannelPrefix = "graph_arc_split";

	public Graph Graph => Vector;

	public EditableGraph(Any<Graph> graph, OverlayChannels overlayChannels, IVectorManager vectorManager, Func<EditOperation, bool, bool> executeEditOperation)
		: base("Graph", "graph_arc_split", graph, overlayChannels, vectorManager, executeEditOperation)
	{
	}
}
