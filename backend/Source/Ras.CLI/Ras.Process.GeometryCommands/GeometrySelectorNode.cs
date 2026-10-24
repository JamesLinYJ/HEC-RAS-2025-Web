using System.Collections.Generic;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Utility.Filtering;

namespace Ras.Process.GeometryCommands;

internal sealed class GeometrySelectorNode : ISelectorNode
{
	private readonly TopologySelectorNode _topology;

	private readonly GeometryFields _fields;

	public string Name => "geometry";

	public ISelectorFields Fields => _fields;

	public IEnumerable<string> MemberNames => new string[1] { _topology.Name };

	public GeometrySelectorNode(Geometry geometry)
	{
		_topology = new TopologySelectorNode(geometry.ConceptualMeshLayer.Topology);
		_fields = new GeometryFields(geometry);
	}

	private GeometrySelectorNode(Topology topology)
	{
		_topology = new TopologySelectorNode(topology);
		_fields = new GeometryFields(null);
	}

	public static GeometrySelectorNode CreateSchema()
	{
		Topology topology = new Topology();
		topology.EnsureMeshMetadataColumns();
		return new GeometrySelectorNode(topology);
	}

	public bool TryGetChildNode(string normalizedName, out ISelectorNode node)
	{
		if (normalizedName == FilterNames.Normalize(_topology.Name))
		{
			node = _topology;
			return true;
		}
		node = null;
		return false;
	}

	public bool TryGetCollection(string normalizedName, out ISelectorCollection collection)
	{
		collection = null;
		return false;
	}
}
