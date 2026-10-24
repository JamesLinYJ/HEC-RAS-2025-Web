using System;

namespace Geospatial.Vectors;

public class ColinearTriangleException : Exception
{
	public Point PointA;

	public Point PointB;

	public Point PointC;

	public ColinearTriangleException(Point pointA, Point pointB, Point pointC)
		: base("Triangulation Error: Colinear Points.")
	{
		PointA = pointA;
		PointB = pointB;
		PointC = pointC;
	}
}
