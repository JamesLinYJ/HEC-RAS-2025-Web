namespace Geospatial.Vectors.MeshTopology;

public class CoonsBase
{
	public Polyline c0;

	public Polyline c1;

	public Polyline d0;

	public Polyline d1;

	public CoonsBase(Polyline south, Polyline east, Polyline north, Polyline west)
	{
		c0 = south;
		d1 = east;
		c1 = north.Clone();
		c1.Reverse();
		d0 = west.Clone();
		d0.Reverse();
	}
}
