using System.Collections.Generic;
using Utility.DataStructures;

namespace Geospatial.Vectors;

public static class VectorExtensionMethods
{
	public static double[,] ToXYArray(this IList<Point> pts)
	{
		double[,] doubleValues = new double[pts.Count, 2];
		for (int i = 0; i < pts.Count; i++)
		{
			Point point = pts[i];
			doubleValues[i, 0] = point.X;
			doubleValues[i, 1] = point.Y;
		}
		return doubleValues;
	}

	public static double[,] ToXYArray(this IArray<Point> pts)
	{
		double[,] doubleValues = new double[pts.Count, 2];
		for (int i = 0; i < pts.Count; i++)
		{
			Point point = pts[i];
			doubleValues[i, 0] = point.X;
			doubleValues[i, 1] = point.Y;
		}
		return doubleValues;
	}
}
