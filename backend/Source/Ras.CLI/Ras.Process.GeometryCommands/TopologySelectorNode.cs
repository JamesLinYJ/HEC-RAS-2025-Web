using System.Collections.Generic;
using Geospatial.Vectors.Topology;
using Utility.Filtering;

namespace Ras.Process.GeometryCommands;

internal sealed class TopologySelectorNode : ISelectorNode
{
	private readonly RegionSelectorCollection _regions;

	private readonly ArcSelectorCollection _arcs;

	public string Name => "topology";

	public ISelectorFields Fields => null;

	public IEnumerable<string> MemberNames => new string[2] { "region", "arc" };

	public TopologySelectorNode(Topology topology)
	{
		_regions = new RegionSelectorCollection(topology);
		_arcs = new ArcSelectorCollection(topology);
	}

	public bool TryGetChildNode(string normalizedName, out ISelectorNode node)
	{
		node = null;
		return false;
	}

	public bool TryGetCollection(string normalizedName, out ISelectorCollection collection)
	{
		switch (normalizedName)
		{
		case "region":
		case "regions":
			collection = _regions;
			return true;
		case "arc":
		case "arcs":
			collection = _arcs;
			return true;
		default:
			collection = null;
			return false;
		}
	}
}
