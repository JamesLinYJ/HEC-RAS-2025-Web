using Geospatial.Vectors.Topology;

namespace Ras.Layers;

public sealed class ConceptualMeshLayer : TopologyLayer, IGeometryLayer, IFeatureLayer
{
	public Geometry Geometry { get; }

	internal ConceptualMeshLayer(Geometry geom)
		: base((TopologyLayer model) => (model as ConceptualMeshLayer).Geometry?.Name)
	{
		Geometry = geom;
	}

	public override void EnsureMetadataColumns(Topology topo)
	{
		topo.EnsureMeshMetadataColumns();
	}

	public static void SaveTopologyToNewGeometry(string filename, Topology topo)
	{
		Geometry geometry = Geometry.CreateNew(filename);
		geometry.ConceptualMeshLayer.Topology = topo;
		geometry.Save();
	}
}
