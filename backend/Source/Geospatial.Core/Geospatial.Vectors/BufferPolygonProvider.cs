namespace Geospatial.Vectors;

public class BufferPolygonProvider : IBufferPolygon
{
	public Polygon Buffer(Polygon input, double distance)
	{
		return input;
	}
}
