using System.Collections;
using System.Collections.Generic;
using Geospatial.Vectors.Topology;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public class TopologyArcsWrapper : IReadOnlyList<ArcMetadata>, IEnumerable<ArcMetadata>, IEnumerable, IReadOnlyCollection<ArcMetadata>
{
	private readonly Topology _topology;

	public string Name => "Arc Metadata";

	public ArcMetadata this[int index] => _topology?.GetArcData(index) ?? default(ArcMetadata);

	public int Count => (_topology?.Arcs?.Count).GetValueOrDefault();

	public TopologyArcsWrapper(Topology topology)
	{
		_topology = topology;
	}

	public IEnumerator<ArcMetadata> GetEnumerator()
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
