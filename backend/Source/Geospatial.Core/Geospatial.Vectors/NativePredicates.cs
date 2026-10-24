using System.Runtime.InteropServices;

namespace Geospatial.Vectors;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct NativePredicates : IPredicates
{
	public static double CounterClockwise(Point a, Point b, Point c)
	{
		return Vector.CrossProduct(in a, in b, in c);
	}

	private static bool IsXColinear(Point p1, Point p2, Point p3)
	{
		if (p1.X == p2.X)
		{
			return p2.X == p3.X;
		}
		return false;
	}

	private static bool IsYColinear(Point p1, Point p2, Point p3)
	{
		if (p1.Y == p2.Y)
		{
			return p2.Y == p3.Y;
		}
		return false;
	}

	public static double InCircle(Point p0, Point p1, Point p2, Point point)
	{
		if (Triangle.ComputeCircumCircle(p0, p1, p2).Contains(point) && (!IsXColinear(p0, p1, point) || ((!(point.Y > p0.Y) || !(point.Y > p1.Y)) && (!(point.Y < p0.Y) || !(point.Y < p1.Y)))) && (!IsXColinear(p0, p2, point) || ((!(point.Y > p0.Y) || !(point.Y > p2.Y)) && (!(point.Y < p0.Y) || !(point.Y < p2.Y)))) && (!IsXColinear(p1, p2, point) || ((!(point.Y > p1.Y) || !(point.Y > p2.Y)) && (!(point.Y < p1.Y) || !(point.Y < p2.Y)))) && (!IsYColinear(p0, p1, point) || ((!(point.X > p0.X) || !(point.X > p1.X)) && (!(point.X < p0.X) || !(point.X < p1.X)))) && (!IsYColinear(p0, p2, point) || ((!(point.X > p0.X) || !(point.X > p2.X)) && (!(point.X < p0.X) || !(point.X < p2.X)))) && (!IsYColinear(p1, p2, point) || ((!(point.X > p1.X) || !(point.X > p2.X)) && (!(point.X < p1.X) || !(point.X < p2.X)))))
		{
			return 1.0;
		}
		return -1.0;
	}
}
