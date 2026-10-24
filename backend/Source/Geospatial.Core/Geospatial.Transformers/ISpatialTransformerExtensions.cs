using System;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public static class ISpatialTransformerExtensions
{
	public static Point Transform(this ISpatialTransformer xf, Point pt)
	{
		Point[] points = new Point[1] { pt };
		Point[] pointsArray = new Point[1];
		xf.Transform(points, pointsArray);
		return pointsArray[0];
	}

	public static Point ReverseTransform(this ISpatialTransformer xf, Point pt)
	{
		Point[] points = new Point[1] { pt };
		Point[] pointsArray = new Point[1];
		xf.ReverseTransform(points, pointsArray);
		return pointsArray[0];
	}

	public static Point[] Transform(this ISpatialTransformer xf, Memory<Point> points)
	{
		Point[] pointsArray = new Point[points.Length];
		xf.Transform(points, pointsArray);
		return pointsArray;
	}

	public static Point[] ReverseTransform(this ISpatialTransformer xf, Memory<Point> points)
	{
		Point[] pointsArray = new Point[points.Length];
		xf.ReverseTransform(points, pointsArray);
		return pointsArray;
	}
}
