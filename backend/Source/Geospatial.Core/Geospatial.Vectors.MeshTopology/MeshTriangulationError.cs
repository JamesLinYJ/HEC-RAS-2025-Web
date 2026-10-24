namespace Geospatial.Vectors.MeshTopology;

public class MeshTriangulationError : DynamicMeshException
{
	public Point Location;

	public MeshTriangulationError(string message, Point location)
		: base(message)
	{
		Location = location;
	}
}
