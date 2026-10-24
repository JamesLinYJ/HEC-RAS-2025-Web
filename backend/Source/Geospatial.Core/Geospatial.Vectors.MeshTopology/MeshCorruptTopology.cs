namespace Geospatial.Vectors.MeshTopology;

public class MeshCorruptTopology : DynamicMeshException
{
	public MeshCorruptTopology(string message)
		: base(message)
	{
	}
}
