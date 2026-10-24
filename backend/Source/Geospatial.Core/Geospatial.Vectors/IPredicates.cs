namespace Geospatial.Vectors;

public interface IPredicates
{
	static abstract double CounterClockwise(Point a, Point b, Point c);

	static abstract double InCircle(Point a, Point b, Point c, Point d);
}
