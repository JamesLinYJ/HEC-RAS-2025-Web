using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Utility.Comparers;

namespace Geospatial.Vectors;

internal static class BestLabelPointSearch
{
	public static Point Run(IList<Polygon> rings, Extent ext, double precision, int maxSearchDepth)
	{
		PriorityQueue<(int, Pixel, double), double> priorityQueue = new PriorityQueue<(int, Pixel, double), double>(ByMaxComparer.Instance);
		RasterDefinition baseRdef = RasterDefinition.ComputeBoundingRaster(ext, 9);
		int rows = baseRdef.Rows;
		int cols = baseRdef.Cols;
		double centerToCornerLength = CenterToCornerLength(0);
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				Pixel pixel = new Pixel(i, j);
				Point pt = baseRdef.CellCenter(i, j);
				double signedPointToPolygonDistance = ComplexPolygon.SignedPointToPolygonDistance(rings, pt);
				double priority = signedPointToPolygonDistance + centerToCornerLength;
				priorityQueue.Enqueue((0, pixel, signedPointToPolygonDistance), priority);
			}
		}
		(int, Pixel, double) tuple = (0, new Pixel(0, 0), double.NegativeInfinity);
		Span<Point> pts = stackalloc Point[4];
		Span<double> distances = stackalloc double[4];
		while (priorityQueue.Count > 0)
		{
			if (!priorityQueue.TryDequeue(out var element, out var priority2))
			{
				throw new Exception();
			}
			if (element.Item3 > tuple.Item3)
			{
				tuple = element;
			}
			if (!(priority2 - tuple.Item3 <= precision))
			{
				int elementTerm = element.Item1 + 1;
				if (elementTerm < maxSearchDepth)
				{
					RasterDefinition rasterDefinition = GetRasterForLevel(elementTerm);
					element.Item2.ChildPixels(out var ul, out var ur, out var ll, out var lr);
					double centerToCornerLength2 = CenterToCornerLength(elementTerm);
					pts[0] = rasterDefinition.CellCenter(ul);
					pts[1] = rasterDefinition.CellCenter(ur);
					pts[2] = rasterDefinition.CellCenter(ll);
					pts[3] = rasterDefinition.CellCenter(lr);
					distances.Fill(double.MaxValue);
					ComplexPolygon.SignedPointsToPolygonDistance(rings, pts, distances);
					double distance = distances[0];
					priorityQueue.Enqueue((elementTerm, ul, distance), distance + centerToCornerLength2);
					double distance2 = distances[1];
					priorityQueue.Enqueue((elementTerm, ur, distance2), distance2 + centerToCornerLength2);
					double distance3 = distances[2];
					priorityQueue.Enqueue((elementTerm, ll, distance3), distance3 + centerToCornerLength2);
					double distance4 = distances[3];
					priorityQueue.Enqueue((elementTerm, lr, distance4), distance4 + centerToCornerLength2);
				}
			}
		}
		return GetRasterForLevel(tuple.Item1).CellCenter(tuple.Item2);
		double CellSizePerLevel(int lvl)
		{
			return baseRdef.CellSize / (double)(1 << lvl);
		}
		double CenterToCornerLength(int lvl)
		{
			return CellSizePerLevel(lvl) * 0.707106781185;
		}
		RasterDefinition GetRasterForLevel(int lvl)
		{
			if (lvl == 0)
			{
				return baseRdef;
			}
			return new RasterDefinition(baseRdef.Extent, baseRdef.Rows * (1 << lvl), baseRdef.Cols * (1 << lvl));
		}
	}
}
