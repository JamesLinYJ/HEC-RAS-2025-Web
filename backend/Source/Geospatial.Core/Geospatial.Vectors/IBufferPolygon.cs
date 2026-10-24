namespace Geospatial.Vectors;

public interface IBufferPolygon
{
	Polygon Buffer(Polygon input, double distance);
}
