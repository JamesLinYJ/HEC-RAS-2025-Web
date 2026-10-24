using System.Collections.Generic;

namespace Geospatial.Vectors.Topology;

public static class TopologyExtensions
{
	public static int RemoveExternalArcs(this Topology topo)
	{
		int ordinal = 0;
		for (int countOffset = topo.Arcs.Count - 1; countOffset >= 0; countOffset--)
		{
			Arc arc = topo.Arcs[countOffset];
			if (arc.RegionA == -1 && arc.RegionB == -1)
			{
				topo.RemoveArc(countOffset, liveUpdateTopology: false);
				ordinal++;
			}
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (Arc arc2 in topo.Arcs)
		{
			if (arc2.StartNode >= 0)
			{
				intSet.Add(arc2.StartNode);
			}
			if (arc2.EndNode >= 0)
			{
				intSet.Add(arc2.EndNode);
			}
		}
		List<int> intList = new List<int>();
		for (int i = 0; i < topo.Nodes.Count; i++)
		{
			if (!intSet.Contains(i))
			{
				intList.Add(i);
			}
		}
		if (intList.Count > 0)
		{
			topo.RemoveNodes(intList);
		}
		topo.RebuildNodeToArcPointers();
		return ordinal;
	}
}
