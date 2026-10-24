using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using RasMapperLib.TinHelpers;

namespace RasMapperLib.Utilities;

public class Converter
{
	public static RasterDefinition Convert(RasterM raster)
	{
		return new RasterDefinition(Convert(raster.ComputeExtent().UpperLeft), raster.CellSize, raster.Rows, raster.Cols);
	}

	public static RasterM Convert(RasterDefinition raster)
	{
		return new RasterM(ConvertPtM(raster.Extent.UpperLeft), raster.Rows, raster.Cols, raster.CellSize);
	}

	public static Geospatial.Vectors.Point Convert(PointM pt)
	{
		return new Geospatial.Vectors.Point(pt.X, pt.Y);
	}

	public static Geospatial.Vectors.Point Convert(Point2Double pt)
	{
		return new Geospatial.Vectors.Point(pt.X, pt.Y);
	}

	public static PointM ConvertPtM(Geospatial.Vectors.Point pt)
	{
		return new PointM(pt.X, pt.Y);
	}

	public static Geospatial.Vectors.Point Convert(Point pt)
	{
		return new Geospatial.Vectors.Point(pt.X, pt.Y);
	}

	public static Point Convert(Geospatial.Vectors.Point pt)
	{
		return new Point(pt.X, pt.Y);
	}

	public static List<Geospatial.Vectors.Point> Convert(IList<PointM> pts)
	{
		List<Geospatial.Vectors.Point> points = new List<Geospatial.Vectors.Point>(pts.Count);
		checked
		{
			int count = pts.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				points.Add(Convert(pts[i]));
			}
			return points;
		}
	}

	public static List<PointM> Convert(IList<Geospatial.Vectors.Point> pts)
	{
		List<PointM> pointMs = new List<PointM>(pts.Count);
		checked
		{
			int count = pts.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs.Add(ConvertPtM(pts[i]));
			}
			return pointMs;
		}
	}

	public static PointMs ConvertPts(PointCollection pts)
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = pts.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs.Add(ConvertPtM(pts[i]));
			}
			return pointMs;
		}
	}

	public static PointCollection ConvertPtCollection(PointMs pms)
	{
		PointCollection pointCollection = new PointCollection(pms.Count);
		checked
		{
			int count = pms.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointCollection.Add(Convert(pms[i]));
			}
			return pointCollection;
		}
	}

	public static Geospatial.Vectors.Extent Convert(Extent ext)
	{
		return new Geospatial.Vectors.Extent(ext.Left, ext.Right, ext.Bottom, ext.Top);
	}

	public static Extent Convert(Geospatial.Vectors.Extent ext)
	{
		return new Extent(ext.Right, ext.Left, ext.Top, ext.Bot);
	}

	public static Polygon Convert(Geospatial.Vectors.Polygon poly)
	{
		PointMs pointMs = ConvertPts(poly);
		pointMs.Add(pointMs.First());
		return new Polygon(pointMs);
	}

	public static Geospatial.Vectors.Polygon Convert(Polygon poly)
	{
		PointCollection pointCollection = ConvertPtCollection(poly.CopyPoints());
		pointCollection.RemoveAt(checked(pointCollection.Count - 1));
		return new Geospatial.Vectors.Polygon(pointCollection);
	}

	public static Polygon Convert(ComplexPolygon poly)
	{
		PointMs pointMs = new PointMs();
		List<int> intList = new List<int>();
		int pointMs2Term = 0;
		foreach (Geospatial.Vectors.Polygon ring in poly.Rings)
		{
			intList.Add(pointMs2Term);
			PointMs pointMs2 = ConvertPts(ring);
			pointMs2.Add(pointMs2.First());
			pointMs.AddRange(pointMs2);
			pointMs2Term = checked(pointMs2Term + pointMs2.Count);
		}
		return new Polygon(pointMs, intList.ToArray());
	}

	public static Geospatial.Vectors.Tin Convert(Tin tin)
	{
		PointMs pts = tin.CopyPoints();
		checked
		{
			int[,] intValues = new int[tin.TriangleCount() - 1 + 1, 3];
			int count = tin.TriangleCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				TriangleStruct triangleStruct = tin.Triangle(i);
				intValues[i, 0] = triangleStruct.p0;
				intValues[i, 1] = triangleStruct.p1;
				intValues[i, 2] = triangleStruct.p2;
			}
			return new Geospatial.Vectors.Tin(Convert(pts), intValues);
		}
	}
}
