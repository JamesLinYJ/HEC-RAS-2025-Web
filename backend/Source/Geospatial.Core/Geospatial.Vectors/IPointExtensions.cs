namespace Geospatial.Vectors;

public static class IPointExtensions
{
	public static double DistanceTo(this IPoint a, IPoint b)
	{
		return Point.Distance(a.X, a.Y, b.X, b.Y);
	}
}
