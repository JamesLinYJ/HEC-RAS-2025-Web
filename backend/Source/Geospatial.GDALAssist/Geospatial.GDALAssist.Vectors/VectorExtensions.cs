using System;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.GDALAssist.Vectors;

public static class VectorExtensions
{
	public static Point Reproject(this Point pt, Projection srcProj, Projection destProj)
	{
		Point reference = pt;
		Span<Point> points = new Span<Point>(ref reference);
		Projector.TransformPoints(srcProj, destProj, points);
		return points[0];
	}

	public static Polyline Reproject(this Polyline pl, Projection srcProj, Projection destProj)
	{
		return ReprojectionTransformer.GetOrMake(srcProj, destProj).Transform(pl);
	}

	public static Polygon Reproject(this Polygon pg, Projection srcProj, Projection destProj)
	{
		return ReprojectionTransformer.GetOrMake(srcProj, destProj).Transform(pg);
	}

	public static void ReprojectAll(this Span<Point> points, Projection srcProj, Projection destProj)
	{
		ReprojectionTransformer.GetOrMake(srcProj, destProj).Transform(points, points);
	}

	public static PolylineCollection ReprojectAll(this PolylineCollection collection, Projection srcProj, Projection destProj)
	{
		ReprojectionTransformer orMake = ReprojectionTransformer.GetOrMake(srcProj, destProj);
		PolylineCollection polylineCollection = new PolylineCollection();
		foreach (Polyline transform in orMake.Transform(collection))
		{
			polylineCollection.Add(transform);
		}
		return polylineCollection;
	}

	public static PolygonCollection ReprojectAll(this PolygonCollection collection, Projection srcProj, Projection destProj)
	{
		ReprojectionTransformer orMake = ReprojectionTransformer.GetOrMake(srcProj, destProj);
		PolygonCollection polygonCollection = new PolygonCollection();
		foreach (Polygon transform in orMake.Transform(collection))
		{
			polygonCollection.Add(transform);
		}
		return polygonCollection;
	}

	public static ComplexPolygon Reproject(this ComplexPolygon polygon, Projection srcProj, Projection destProj)
	{
		return ReprojectionTransformer.GetOrMake(srcProj, destProj).Transform(polygon);
	}

	public static ComplexPolygonCollection ReprojectAll(this ComplexPolygonCollection collection, Projection srcProj, Projection destProj)
	{
		ReprojectionTransformer orMake = ReprojectionTransformer.GetOrMake(srcProj, destProj);
		ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection();
		foreach (ComplexPolygon transform in orMake.Transform(collection))
		{
			complexPolygonCollection.Add(transform);
		}
		return complexPolygonCollection;
	}

	public static Tin Reproject(this Tin tin, Projection srcProj, Projection destProj)
	{
		using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(srcProj, destProj);
		Point[] points = new Point[tin.Points.Count];
		reprojectionTransformer.Transform(tin.Points.ToArray().AsSpan(), points);
		return new Tin(points, tin.TriangleDefinition, tin.IsCleaned);
	}

	public static Extent Reproject(this Extent ext, Projection srcProj, Projection destProj)
	{
		return Projector.GetBoundingBox(ext, srcProj, destProj);
	}
}
