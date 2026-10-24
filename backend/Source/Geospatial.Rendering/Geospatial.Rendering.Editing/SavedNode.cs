using System.Diagnostics;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

[DebuggerDisplay("{ToString(),nq}")]
public struct SavedNode
{
	public int FID;

	public Node OriginalNode;

	public SavedArc SurvivedArc;

	public int SurvivedArcCount;

	public bool SurvivedArcReconnectedStart;

	public bool WasMerged => !SurvivedArc.IsNull();

	public SavedNode(int fid, Node originalNode)
	{
		SurvivedArcCount = 0;
		SurvivedArcReconnectedStart = false;
		FID = fid;
		OriginalNode = originalNode;
		SurvivedArc = SavedArc.NullSavedArc;
	}

	public override string ToString()
	{
		return "FID: " + FID + ", NODE: " + OriginalNode.ToString();
	}
}
