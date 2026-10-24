using System.Collections.Generic;

namespace Geospatial.Vectors.Topology;

internal sealed class TopologyCompletionCache
{
	public int[] ArcSpatialHashes { get; private set; }

	public List<Region> Regions { get; private set; }

	public int ArcCount { get; private set; }

	public void Capture(Topology topo)
	{
		IReadOnlyList<Arc> arcs = topo.Arcs;
		int[] arcSpatialHashes = new int[arcs.Count];
		for (int i = 0; i < arcs.Count; i++)
		{
			Arc arc = arcs[i];
			if (arc.StartNode == -1 || arc.EndNode == -1)
			{
				arcSpatialHashes[i] = 0;
			}
			else
			{
				arcSpatialHashes[i] = topo.GetArcSpatialHash(i);
			}
		}
		IReadOnlyList<Region> completedRegions = topo.CompletedRegions;
		List<Region> regions = new List<Region>(completedRegions.Count);
		for (int j = 0; j < completedRegions.Count; j++)
		{
			regions.Add(completedRegions[j].Clone());
		}
		ArcSpatialHashes = arcSpatialHashes;
		Regions = regions;
		ArcCount = arcs.Count;
	}

	public void UpdateArcHash(int arcId, int newHash)
	{
		ArcSpatialHashes[arcId] = newHash;
	}
}
