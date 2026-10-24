using System.Collections;
using System.Collections.Generic;
using Geospatial.Vectors.Topology;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public class TopologyRegionsWrapper : IReadOnlyList<MeshRegionMetadata>, IEnumerable<MeshRegionMetadata>, IEnumerable, IReadOnlyCollection<MeshRegionMetadata>
{
	private readonly Topology _topology;

	public string Name => "Region Metadata";

	public MeshRegionMetadata this[int index] => _topology?.GetRegionData(index) ?? default(MeshRegionMetadata);

	public int Count => (_topology?.CompletedRegions?.Count).GetValueOrDefault();

	public TopologyRegionsWrapper(Topology topology)
	{
		_topology = topology;
	}

	public IEnumerator<MeshRegionMetadata> GetEnumerator()
	{
		for (int i = 0; i < Count; i++)
		{
			yield return this[i];
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
