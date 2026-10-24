using System;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public static class PointGenerator
{
	public static List<Point> FromExtent(Extent baseExt, int cellsW, int cellsH, bool generateHexCells = false)
	{
		List<Point> points = new List<Point>(cellsW * cellsH);
		double widthOverCellsW = baseExt.Width / (double)cellsW;
		double heightOverCellsH = baseExt.Height / (double)cellsH;
		for (int i = 0; i < cellsH; i++)
		{
			double computed = 0.0;
			int cellsW2 = cellsW;
			if (generateHexCells && i % 2 == 1)
			{
				cellsW2--;
				computed = widthOverCellsW * 0.5;
			}
			for (int j = 0; j < cellsW2; j++)
			{
				points.Add(new Point(baseExt.Left + computed + ((double)j + 0.5) * widthOverCellsW, baseExt.Top - ((double)i + 0.5) * heightOverCellsH));
			}
		}
		return points;
	}

	public static PointCollection FromPerimeter(Polygon perim, double cellsize, bool generateHexCells = false)
	{
		double distanceTolerance = cellsize / 10.0;
		double cellsizePolynomial = cellsize * cellsize;
		PointCollection pointCollection = new PointCollection((int)Math.Ceiling(perim.Area / cellsizePolynomial * 1.2));
		Extent extent = perim.Extent;
		Tin devTin = perim.DevTin;
		int widthExtentTerm = (int)Math.Ceiling(extent.Width / cellsize);
		int count = (int)Math.Ceiling(extent.Height / cellsize);
		for (int i = 0; i < count; i++)
		{
			double scaledCellsize = 0.0;
			int count2 = widthExtentTerm;
			if (generateHexCells && i % 2 == 1)
			{
				count2--;
				scaledCellsize = cellsize * 0.5;
			}
			int t = 0;
			for (int j = 0; j < count2; j++)
			{
				Point point = new Point(extent.Left + scaledCellsize + ((double)j + 0.5) * cellsize, extent.Top - ((double)i + 0.5) * cellsize);
				if (devTin.ContainsPointBufferedFavorOutOfBounds(point, distanceTolerance, ref t))
				{
					pointCollection.Add(point);
				}
			}
		}
		return pointCollection;
	}

	public static bool IsPerimeterValidToGeneratePointsFrom(Polygon perimeter)
	{
		if (perimeter.Points.Count < 3)
		{
			return false;
		}
		if (!perimeter.IsValid())
		{
			return false;
		}
		if (perimeter.SelfIntersects())
		{
			return false;
		}
		return true;
	}
}
