namespace Geospatial.Vectors;

public struct Ray
{
	public Point Point;

	public Vector Vector;

	public Ray(Point pt, Vector vec)
	{
		Point = pt;
		Vector = vec;
		Vector.Normalize();
	}
}
